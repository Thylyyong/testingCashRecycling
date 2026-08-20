using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Infrastructure.Data;
using SelfCheckoutKiosk.Infrastructure.Sync;
using Xunit;

namespace SelfCheckoutKiosk.Infrastructure.Tests;

public sealed class TailscaleSyncWorkerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"kiosk-sync-{Guid.NewGuid():N}.db");

    private sealed class FakeErpSyncClient : IErpSyncClient
    {
        public List<TransactionSyncDto> PushedTransactions { get; } = [];
        public bool AcknowledgePush { get; set; } = true;
        public IReadOnlyList<ProductCatalogUpdateDto> CatalogUpdatesToReturn { get; set; } = [];

        public Task<bool> PushTransactionAsync(TransactionSyncDto transaction, CancellationToken cancellationToken)
        {
            PushedTransactions.Add(transaction);
            return Task.FromResult(AcknowledgePush);
        }

        public Task<IReadOnlyList<ProductCatalogUpdateDto>> PullCatalogUpdatesAsync(CancellationToken cancellationToken)
            => Task.FromResult(CatalogUpdatesToReturn);
    }

    [Fact]
    public async Task PushPendingTransactionsAsync_PushesAndMarksCompleted_OnlyWhenAcknowledged()
    {
        Guid pendingGuid;
        using (var context = new KioskDbContext(_dbPath, "correct-horse-battery-staple"))
        {
            context.EnsureSchemaCreated();

            var pending = new Transaction { TotalUsd = 5.50m, TenderedUsd = 5.50m, SyncStatus = SyncStatus.Pending };
            pending.LineItems.Add(new LineItem { Ean13 = "8850001100018", Description = "Coca-Cola 330ml Can", UnitPriceUsd = 5.50m });
            pendingGuid = pending.TransactionGuid;

            var alreadyCompleted = new Transaction { TotalUsd = 1.00m, TenderedUsd = 1.00m, SyncStatus = SyncStatus.Completed };

            context.Transactions.AddRange(pending, alreadyCompleted);
            context.SaveChanges();
        }

        var erpClient = new FakeErpSyncClient();
        var worker = new TailscaleSyncWorker(() => new KioskDbContext(_dbPath, "correct-horse-battery-staple"), erpClient, TimeSpan.FromMinutes(5));

        await worker.PushPendingTransactionsAsync(CancellationToken.None);

        Assert.Single(erpClient.PushedTransactions);
        Assert.Equal(pendingGuid, erpClient.PushedTransactions[0].TransactionGuid);
        Assert.Single(erpClient.PushedTransactions[0].LineItems);

        using var verifyContext = new KioskDbContext(_dbPath, "correct-horse-battery-staple");
        Transaction? reloaded = verifyContext.Transactions.Find(pendingGuid);
        Assert.Equal(SyncStatus.Completed, reloaded!.SyncStatus);
    }

    [Fact]
    public async Task PushPendingTransactionsAsync_LeavesTransactionPending_WhenErpDoesNotAcknowledge()
    {
        Guid pendingGuid;
        using (var context = new KioskDbContext(_dbPath, "correct-horse-battery-staple"))
        {
            context.EnsureSchemaCreated();
            var pending = new Transaction { TotalUsd = 2.00m, TenderedUsd = 2.00m, SyncStatus = SyncStatus.Pending };
            pendingGuid = pending.TransactionGuid;
            context.Transactions.Add(pending);
            context.SaveChanges();
        }

        var erpClient = new FakeErpSyncClient { AcknowledgePush = false };
        var worker = new TailscaleSyncWorker(() => new KioskDbContext(_dbPath, "correct-horse-battery-staple"), erpClient, TimeSpan.FromMinutes(5));

        await worker.PushPendingTransactionsAsync(CancellationToken.None);

        using var verifyContext = new KioskDbContext(_dbPath, "correct-horse-battery-staple");
        Transaction? reloaded = verifyContext.Transactions.Find(pendingGuid);
        Assert.Equal(SyncStatus.Pending, reloaded!.SyncStatus); // still pending — will retry next cycle
    }

    [Fact]
    public async Task PullCatalogUpdatesAsync_UpsertsNewAndExistingProducts()
    {
        using (var context = new KioskDbContext(_dbPath, "correct-horse-battery-staple"))
        {
            context.EnsureSchemaCreated();
            context.Products.Add(new Product { Ean13 = "8850001100018", Description = "Stale Name", UsdPrice = 0.50m, KhrPrice = 2000m });
            context.SaveChanges();
        }

        var erpClient = new FakeErpSyncClient
        {
            CatalogUpdatesToReturn =
            [
                new ProductCatalogUpdateDto("8850001100018", "Coca-Cola 330ml Can", 0.75m, 3000m), // price update
                new ProductCatalogUpdateDto("4801981102901", "Bottled Water 500ml", 0.35m, 1400m),  // brand new SKU
            ],
        };
        var worker = new TailscaleSyncWorker(() => new KioskDbContext(_dbPath, "correct-horse-battery-staple"), erpClient, TimeSpan.FromMinutes(5));

        await worker.PullCatalogUpdatesAsync(CancellationToken.None);

        using var verifyContext = new KioskDbContext(_dbPath, "correct-horse-battery-staple");
        Product? updated = verifyContext.Products.Find("8850001100018");
        Assert.Equal("Coca-Cola 330ml Can", updated!.Description);
        Assert.Equal(0.75m, updated.UsdPrice);

        Product? inserted = verifyContext.Products.Find("4801981102901");
        Assert.NotNull(inserted);
        Assert.Equal("Bottled Water 500ml", inserted!.Description);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}
