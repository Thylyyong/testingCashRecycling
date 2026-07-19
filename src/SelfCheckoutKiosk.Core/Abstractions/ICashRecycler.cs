using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Strategy contract for a cash-recycler peripheral (Blueprint §4).
/// A new vendor SKU is a NEW adapter project implementing this interface —
/// never a change to Core. Core logic depends on this abstraction only.
/// TODO(Back-End): extend surface as vendor SDK realities emerge.
/// </summary>
public interface ICashRecycler
{
    /// <summary>Raised the instant a note lands in escrow. The engine's
    /// HardwareAppendLog subscribes to this synchronously (Blueprint §4).</summary>
    event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;

    event EventHandler<HardwareFaultEventArgs>? OnFault;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task ArmAcceptanceAsync(CancellationToken cancellationToken = default);
    Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Hard stop on cash intake — driven by the low-float safeguard.</summary>
    Task StopAcceptingCashAsync(CancellationToken cancellationToken = default);

    Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken cancellationToken = default);
    Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default);
}
