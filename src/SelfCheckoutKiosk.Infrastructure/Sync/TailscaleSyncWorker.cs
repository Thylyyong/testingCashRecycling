namespace SelfCheckoutKiosk.Infrastructure.Sync;

/// <summary>
/// STUB — background sync worker (Blueprint §4). Runs inside the Tailscale
/// overlay (no public ports); polls Transactions where SyncStatus=Pending,
/// pushes to the central server, marks Completed on ack (idempotent via
/// TransactionGuid).
///
/// SECURITY (Lead): segment this tunnel from the tunneled-RDP support channel —
/// separate ACLs / tailnet lock, least privilege.
/// TODO(Back-End): implement as IHostedService once Hosting is added.
/// </summary>
public sealed class TailscaleSyncWorker
{
    public Task RunOnceAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Back-End): implement pending->completed push.");
}
