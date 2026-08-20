using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Raised immediately when a physical note enters the device's
/// escrow position.
///
/// The note has not yet been treated by Core as committed to the
/// vault or returned to the customer.
/// </summary>
public sealed class NoteInEscrowEventArgs(Money note) : EventArgs
{
    /// <summary>
    /// Currency and denomination of the note currently held in
    /// escrow.
    /// </summary>
    public Money Note { get; } = note;
}

/// <summary>
/// Raised when a physical note has been detected entering the cash device.
/// The note is not yet treated as accepted money.
/// </summary>
public sealed class NoteInsertedEventArgs(Money note) : EventArgs
{
    public Money Note { get; } = note;
}

/// <summary>
/// Customer-facing readiness of the cash acceptor.
/// </summary>
public enum CashAcceptorState
{
    Inactive = 0,
    Activating = 1,
    Ready = 2,
    Error = 3
}

public sealed class CashAcceptorStateChangedEventArgs(CashAcceptorState state) : EventArgs
{
    public CashAcceptorState State { get; } = state;
}

/// <summary>
/// Raised when the cash device reports a physical jam condition.
/// </summary>
public sealed class CashRecyclerJamEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

/// <summary>
/// Physical outcome of an escrowed cash note.
/// </summary>
public enum CashEscrowResolution
{
    /// <summary>
    /// The note was physically moved from escrow into accepted cash storage.
    /// </summary>
    CommittedToVault = 0,

    /// <summary>
    /// The note was physically returned to the customer.
    /// </summary>
    Rejected = 1,

    /// <summary>
    /// Backward-compatible alias for CommittedToVault.
    /// </summary>
    Committed = 0
}

/// <summary>
/// Raised only after the cash device confirms the physical outcome of an escrowed note.
/// </summary>
public sealed class CashEscrowResolvedEventArgs(Money note, CashEscrowResolution resolution) : EventArgs
{
    public Money Note { get; } = note;
    public CashEscrowResolution Resolution { get; } = resolution;
}

public sealed class BarcodeScannedEventArgs(string rawBarcode) : EventArgs
{
    public string RawBarcode { get; } = rawBarcode;
}

public enum PrintJobState
{
    Queued = 0,
    Printing = 1,
    Completed = 2,
    Failed = 3
}

public sealed class PrintJobStatusEventArgs(PrintJobState state) : EventArgs
{
    public PrintJobState State { get; } = state;
}

public sealed class HardwareFaultEventArgs(string device, string message) : EventArgs
{
    public string Device { get; } = device;
    public string Message { get; } = message;
}

/// <summary>
/// Raised whenever the cash recycler's physical cassette counts change.
/// </summary>
public sealed class CassetteInventoryChangedEventArgs(IReadOnlyDictionary<int, int> countsByKhrDenomination) : EventArgs
{
    public IReadOnlyDictionary<int, int> CountsByKhrDenomination { get; } = countsByKhrDenomination;
}

/// <summary>
/// Outcome of a physical dispense command.
/// </summary>
public sealed record DispenseResult(bool Success, ChangeBreakdown Dispensed);
