using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;

// TODO(Back-End/Systems): remove once real device events are wired.
#pragma warning disable CS0067 // Event is declared but never raised (stub)

/// <summary>STUB adapter for a Datalogic scanner over USB-COM virtual serial.</summary>
public sealed class DatalogicBarcodeScanner : IBarcodeScanner
{
    public event EventHandler<BarcodeScannedEventArgs>? OnBarcodeScanned;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException("TODO: open serial port.");
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
