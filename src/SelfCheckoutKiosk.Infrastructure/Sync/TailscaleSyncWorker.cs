using Microsoft.Extensions.Hosting;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Infrastructure.Data;

namespace SelfCheckoutKiosk.Infrastructure.Sync;

/// <summary>
/// Background sync worker (Blueprint §4). Runs inside the Tailscale overlay
/// (no public ports) on a fixed poll interval:
///   Push: Transactions where SyncStatus=Pending -> ERP; mark Completed on
///         ack. Idempotent via TransactionGuid — a retried push after a
///         dropped ack acknowledgment is a no-op on both sides, never a
///         duplicate sale.
///   Pull: ERP catalog master updates (new SKUs, price changes) -> local
///         Products table, upserted by EAN-13.
///
/// A single sync cycle failing (Tailscale link down, ERP unreachable) is the
/// expected steady state for an offline-first kiosk, not an error — it must
/// never crash the worker or block the next cycle.
///
/// SECURITY (Lead): segment this tunnel from the tunneled-RDP support
/// channel — separate ACLs / tailnet lock, least privilege.
/// </summary>
public sealed class TailscaleSyncWorker(
    Func<KioskDbContext> dbContextFactory,
    IErpSyncClient erpClient,
    TimeSpan pollInterval) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PushPendingTransactionsAsync(stoppingToken).ConfigureAwait(false);
                await PullCatalogUpdatesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // TODO(Lead): route to real diagnostics/telemetry once one
                // exists. Deliberately swallowed otherwise — see class doc.
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown mid-delay — fall through, the while condition ends the loop.
            }
        }
    }

    /// <summary>One push cycle, exposed publicly so it's testable and
    /// callable standalone (e.g. a "sync now" UI action) without waiting for
    /// the poll interval.</summary>
    public async Task PushPendingTransactionsAsync(CancellationToken cancellationToken)
    {
        using KioskDbContext db = dbContextFactory();
        IReadOnlyList<Transaction> pending = db.GetPendingTransactions();

        foreach (Transaction transaction in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool acknowledged = await erpClient.PushTransactionAsync(ToDto(transaction), cancellationToken).ConfigureAwait(false);
            if (acknowledged)
                db.MarkTransactionCompleted(transaction.TransactionGuid);
        }
    }

    /// <summary>One pull cycle, exposed publicly for the same reason as
    /// <see cref="PushPendingTransactionsAsync"/>.</summary>
    public async Task PullCatalogUpdatesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductCatalogUpdateDto> updates = await erpClient.PullCatalogUpdatesAsync(cancellationToken).ConfigureAwait(false);
        if (updates.Count == 0) return;

        using KioskDbContext db = dbContextFactory();
        foreach (ProductCatalogUpdateDto update in updates)
            db.UpsertProduct(update.Ean13, update.Description, update.UsdPrice, update.KhrPrice);
    }

    private static TransactionSyncDto ToDto(Transaction transaction) => new(
        transaction.TransactionGuid,
        transaction.CreatedAtUtc,
        transaction.TotalUsd,
        transaction.TenderedUsd,
        transaction.PaymentMethod is { } method ? (int)method : null,
        transaction.LineItems
            .Select(li => new LineItemSyncDto(li.Ean13, li.Description, li.UnitPriceUsd, li.Quantity))
            .ToArray());
}
