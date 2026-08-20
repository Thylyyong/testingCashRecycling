using Microsoft.Data.Sqlite;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Infrastructure.Data;
using Xunit;

namespace SelfCheckoutKiosk.Infrastructure.Tests;

/// <summary>
/// Covers the three things Sprint 0's placeholder flagged as missing:
/// WAL/FULL pragma verification and a SQLCipher round-trip. Orphaned-escrow
/// reconciliation on startup belongs to the engine/sync work package, not
/// this persistence layer, and is tracked separately.
/// </summary>
public sealed class KioskDbContextTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"kiosk-{Guid.NewGuid():N}.db");

    [Fact]
    public void OnConfiguring_SetsWalAndFullSynchronousPragmas()
    {
        using var context = new KioskDbContext(_dbPath, "correct-horse-battery-staple");
        context.Database.EnsureCreated();

        using SqliteConnection verifyConnection = new(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Password = "correct-horse-battery-staple",
        }.ConnectionString);
        verifyConnection.Open();

        Assert.Equal("wal", QueryScalar(verifyConnection, "PRAGMA journal_mode;"));
        Assert.Equal("2", QueryScalar(verifyConnection, "PRAGMA synchronous;")); // 2 == FULL
    }

    [Fact]
    public void Database_IsEncrypted_WrongPassphraseCannotOpenIt()
    {
        using (var context = new KioskDbContext(_dbPath, "right-passphrase"))
        {
            context.Database.EnsureCreated();
            context.Products.Add(new Product { Ean13 = "0000000000017", Description = "Test Item", UsdPrice = 1.00m });
            context.SaveChanges();
        }

        // Pooling=false so the failed connection's native handle is released
        // the instant it's disposed, rather than lingering in the pool keyed
        // by this (now-useless) connection string until ClearAllPools runs.
        using SqliteConnection wrongPassword = new(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Password = "wrong-passphrase",
            Pooling = false,
        }.ConnectionString);

        // A SQLCipher-encrypted file opened with the wrong key fails on first
        // real access — it is not a valid SQLite database to that key.
        var ex = Assert.ThrowsAny<SqliteException>(() =>
        {
            wrongPassword.Open();
            using var cmd = wrongPassword.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Products;";
            cmd.ExecuteScalar();
        });
        Assert.NotNull(ex);
    }

    [Fact]
    public void SavedTransaction_RoundTripsLineItemsAndSyncStatus()
    {
        var transactionGuid = Guid.NewGuid();

        using (var context = new KioskDbContext(_dbPath, "correct-horse-battery-staple"))
        {
            context.Database.EnsureCreated();
            var transaction = new Transaction
            {
                TransactionGuid = transactionGuid,
                TotalUsd = 5.50m,
                TenderedUsd = 5.50m,
                SyncStatus = SyncStatus.Pending,
            };
            transaction.LineItems.Add(new LineItem { Ean13 = "0000000000017", Description = "Test Item", UnitPriceUsd = 5.50m });
            context.Transactions.Add(transaction);
            context.SaveChanges();
        }

        using (var context = new KioskDbContext(_dbPath, "correct-horse-battery-staple"))
        {
            Transaction? reloaded = context.Transactions.Find(transactionGuid);
            Assert.NotNull(reloaded);
            Assert.Equal(SyncStatus.Pending, reloaded!.SyncStatus);
        }
    }

    [Fact]
    public void CatalogSeeder_SeedsProducts_AndIsIdempotent()
    {
        using var context = new KioskDbContext(_dbPath, "correct-horse-battery-staple");
        context.EnsureSchemaCreated();

        CatalogSeeder.SeedIfEmpty(context);
        int firstSeedCount = context.Products.Count();

        Assert.True(firstSeedCount >= 5);

        CatalogSeeder.SeedIfEmpty(context); // second call must be a no-op
        Assert.Equal(firstSeedCount, context.Products.Count());
    }

    [Fact]
    public void EfProductCatalog_FindsSeededProductByEan13_AndReturnsNullForUnknown()
    {
        using (var context = new KioskDbContext(_dbPath, "correct-horse-battery-staple"))
        {
            context.EnsureSchemaCreated();
            CatalogSeeder.SeedIfEmpty(context);
        }

        var catalog = new EfProductCatalog(() => new KioskDbContext(_dbPath, "correct-horse-battery-staple"));

        Product? found = catalog.FindByEan13("8850001100018"); // Coca-Cola 330ml Can, seeded
        Assert.NotNull(found);
        Assert.Equal("Coca-Cola 330ml Can", found!.Description);
        Assert.Equal(0.75m, found.UsdPrice);
        Assert.Equal(3000m, found.KhrPrice);

        Assert.Null(catalog.FindByEan13("0000000000000"));
    }

    private static string? QueryScalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
        {
            // Best-effort: WAL mode memory-maps the -shm file, and Windows can
            // hold the underlying handle open briefly after the logical
            // SQLite close. This is unrelated to what the test actually
            // verifies, so a lingering temp file must not fail the test.
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
