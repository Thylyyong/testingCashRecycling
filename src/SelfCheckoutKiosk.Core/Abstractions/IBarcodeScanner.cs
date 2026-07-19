namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Strategy contract for a barcode scanner over a USB-COM virtual serial
/// port (Blueprint §4). Emits raw scan strings; classification is the
/// engine's RegexRouter responsibility, not the adapter's.
/// TODO(Back-End): extend surface as vendor SDK realities emerge.
/// </summary>
public interface IBarcodeScanner
{
    event EventHandler<BarcodeScannedEventArgs>? OnBarcodeScanned;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
