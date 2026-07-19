using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

// TODO(Back-End/Systems): remove once real device events are wired.
#pragma warning disable CS0067 // Event is declared but never raised (stub)

/// <summary>STUB adapter for the "CashRecyclerX" SKU. Implements ICashRecycler
/// against the real vendor SDK. No business logic lives here.</summary>
public sealed class VendorXCashRecycler : ICashRecycler
{
    public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
    public event EventHandler<HardwareFaultEventArgs>? OnFault;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException("TODO: vendor SDK connect.");
    public Task ArmAcceptanceAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task StopAcceptingCashAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
