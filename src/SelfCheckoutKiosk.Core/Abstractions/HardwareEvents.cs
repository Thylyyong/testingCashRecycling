using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Abstractions;

public sealed class NoteInEscrowEventArgs(Money note) : EventArgs
{
    public Money Note { get; } = note;
}

public sealed class BarcodeScannedEventArgs(string rawBarcode) : EventArgs
{
    public string RawBarcode { get; } = rawBarcode;
}

public enum PrintJobState { Queued, Printing, Completed, Failed }

public sealed class PrintJobStatusEventArgs(PrintJobState state) : EventArgs
{
    public PrintJobState State { get; } = state;
}

public sealed class HardwareFaultEventArgs(string device, string message) : EventArgs
{
    public string Device { get; } = device;
    public string Message { get; } = message;
}

/// <summary>Outcome of a physical dispense command.</summary>
public sealed record DispenseResult(bool Success, ChangeBreakdown Dispensed);
