using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Raised immediately when a physical note enters the device's
/// escrow position.
///
/// The note has not yet been treated by Core as committed to the
/// vault or returned to the customer.
/// </summary>
public sealed class NoteInEscrowEventArgs(
    Money note)
    : EventArgs
{
    /// <summary>
    /// Currency and denomination of the note currently held in
    /// escrow.
    /// </summary>
    public Money Note
    {
        get;
    } = note;
}

/// <summary>
/// Physical outcome of an escrowed cash note.
/// </summary>
public enum CashEscrowResolution
{
    /// <summary>
    /// The note was physically moved from escrow into accepted cash
    /// storage.
    /// </summary>
    CommittedToVault = 0,

    /// <summary>
    /// The note was physically returned to the customer.
    /// </summary>
    Rejected = 1
}

/// <summary>
/// Raised only after the cash device confirms the physical outcome
/// of an escrowed note.
/// </summary>
public sealed class CashEscrowResolvedEventArgs(
    Money note,
    CashEscrowResolution resolution)
    : EventArgs
{
    public Money Note
    {
        get;
    } = note;

    public CashEscrowResolution Resolution
    {
        get;
    } = resolution;
}

public sealed class BarcodeScannedEventArgs(
    string rawBarcode)
    : EventArgs
{
    public string RawBarcode
    {
        get;
    } = rawBarcode;
}

public enum PrintJobState
{
    Queued = 0,
    Printing = 1,
    Completed = 2,
    Failed = 3
}

public sealed class PrintJobStatusEventArgs(
    PrintJobState state)
    : EventArgs
{
    public PrintJobState State
    {
        get;
    } = state;
}

public sealed class HardwareFaultEventArgs(
    string device,
    string message)
    : EventArgs
{
    public string Device
    {
        get;
    } = device;

    public string Message
    {
        get;
    } = message;
}

/// <summary>
/// Outcome of a physical dispense command.
/// </summary>
public sealed record DispenseResult(
    bool Success,
    ChangeBreakdown Dispensed);