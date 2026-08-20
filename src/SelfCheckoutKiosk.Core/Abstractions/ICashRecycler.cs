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
    /// Raised when the device detects a note entering its note path.
    /// Detection does not mean that the note has been accepted.
    /// </summary>
    event EventHandler<NoteInsertedEventArgs>? OnNoteInserted;

    /// <summary>
    /// Raised immediately when a physical cash note enters the device's escrow position.
    /// </summary>
    event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;

    /// <summary>
    /// Raised when the cash recycler reports a hardware fault.
    /// </summary>
    event EventHandler<HardwareFaultEventArgs>? OnFault;

    /// <summary>
    /// Raised when the acceptor moves between inactive, activating, ready, and error states.
    /// </summary>
    event EventHandler<CashAcceptorStateChangedEventArgs>? OnAcceptorStateChanged;

    /// <summary>
    /// Raised when the device reports a physical cash-path jam.
    /// </summary>
    event EventHandler<CashRecyclerJamEventArgs>? OnJam;

    /// <summary>
    /// Raised whenever physical cassette counts change (dispense, commit-to-vault, or periodic inventory poll).
    /// </summary>
    event EventHandler<CassetteInventoryChangedEventArgs>? OnCassetteInventoryChanged;

    /// <summary>
    /// Connects to the physical cash recycler.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops device activity and closes the physical connection.
    /// Repeated calls must be safe.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables cash-note acceptance.
    /// </summary>
    Task ArmAcceptanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables normal cash-note acceptance.
    /// </summary>
    Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately stops further cash intake.
    /// </summary>
    Task StopAcceptingCashAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispenses the requested change.
    /// </summary>
    Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commands the device to physically return the currently escrowed note to the customer.
    /// </summary>
    Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default);
}
