using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Strategy contract for a cash-recycler peripheral.
///
/// A vendor-specific cash device implements this interface in
/// its HAL adapter project. Core depends only on this contract
/// and never on a concrete vendor SDK.
/// </summary>
public interface ICashRecycler
{
    /// <summary>
    /// Raised immediately when a physical cash note enters the
    /// device's escrow position.
    ///
    /// LLCoreLogicEngine is the single subscriber to this HAL
    /// event. The engine synchronously writes the HardwareAppendLog
    /// ESCROW record before performing any further processing of
    /// the note.
    /// </summary>
    event EventHandler<NoteInEscrowEventArgs>?
        OnNoteInEscrow;

    /// <summary>
    /// Raised when the cash recycler reports a hardware fault.
    /// </summary>
    event EventHandler<HardwareFaultEventArgs>?
        OnFault;

    /// <summary>
    /// Connects to the physical cash recycler.
    /// </summary>
    Task ConnectAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables cash-note acceptance.
    /// </summary>
    Task ArmAcceptanceAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables normal cash-note acceptance.
    /// </summary>
    Task DisarmAcceptanceAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately stops further cash intake.
    ///
    /// Used by low-float protection and fault handling.
    /// </summary>
    Task StopAcceptingCashAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispenses the requested change.
    /// </summary>
    Task<DispenseResult> DispenseAsync(
        ChangeBreakdown change,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commands the device to physically return the currently
    /// escrowed note to the customer.
    /// </summary>
    Task RejectEscrowedNoteAsync(
        CancellationToken cancellationToken = default);
}