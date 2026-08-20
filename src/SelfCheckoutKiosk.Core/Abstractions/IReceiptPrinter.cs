namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Strategy contract for a raw ESC/POS receipt printer, e.g. Epson m30
/// (Blueprint §4).
/// TODO(Back-End): extend surface as vendor SDK realities emerge.
/// </summary>
public interface IReceiptPrinter
{
    bool IsConnected { get; }
    event EventHandler<PrintJobStatusEventArgs>? OnJobStatusChanged;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task<bool> IsPaperPresentAsync(CancellationToken cancellationToken = default);
    Task PrintRawAsync(ReadOnlyMemory<byte> escPosPayload, CancellationToken cancellationToken = default);
}
