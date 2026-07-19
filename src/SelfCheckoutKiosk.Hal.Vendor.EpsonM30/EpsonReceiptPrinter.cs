using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

// TODO(Back-End/Systems): remove once real job-status events are wired.
#pragma warning disable CS0067 // Event is declared but never raised (stub)

/// <summary>STUB adapter for the Epson m30 raw ESC/POS receipt printer.</summary>
public sealed class EpsonReceiptPrinter : IReceiptPrinter
{
    public event EventHandler<PrintJobStatusEventArgs>? OnJobStatusChanged;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException("TODO: open printer channel.");
    public Task<bool> IsPaperPresentAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task PrintRawAsync(ReadOnlyMemory<byte> escPosPayload, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
