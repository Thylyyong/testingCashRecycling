using System;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;

/// <summary>
/// HAL adapter for a Datalogic barcode scanner over USB-COM virtual serial port.
/// Supports 1D barcodes (EAN/UPC/Code128) and 2D QR codes with streaming buffer parsing.
/// </summary>
public sealed class DatalogicBarcodeScanner : IBarcodeScanner, IAsyncDisposable
{
    private SerialPort?              _port;
    private CancellationTokenSource? _cts;
    private string                   _comPort;
    private readonly int             _baudRate;

    public bool IsConnected => _port?.IsOpen == true;

    public event EventHandler<BarcodeScannedEventArgs>? OnBarcodeScanned;

    public DatalogicBarcodeScanner(string comPort = "COM4", int baudRate = 9600)
    {
        _comPort  = comPort;
        _baudRate = baudRate;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_port?.IsOpen == true)
            return Task.CompletedTask;

        // Try opening configured COM port first
        if (TryOpenPort(_comPort))
            return Task.CompletedTask;

        // If configured port fails, probe available serial ports (skipping COM3 which is the Cash Recycler)
        try
        {
            var activePorts = SerialPort.GetPortNames().Distinct();
            foreach (var p in activePorts)
            {
                if (p.Equals(_comPort, StringComparison.OrdinalIgnoreCase)) continue;
                if (p.Equals("COM3", StringComparison.OrdinalIgnoreCase)) continue; // Reserved for Cash Recycler REST API

                if (TryOpenPort(p))
                {
                    _comPort = p;
                    return Task.CompletedTask;
                }
            }
        }
        catch { }

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[DatalogicBarcodeScanner] USB-COM port not detected (Scanner may be in USB-HID Keyboard mode or offline). Ready for scan events.");
        Console.ResetColor();

        return Task.CompletedTask;
    }

    private bool TryOpenPort(string portName)
    {
        try
        {
            var p = new SerialPort(portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout  = 500,
                WriteTimeout = 500
            };
            p.Open();
            _port = p;

            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            _ = Task.Run(() => ReadLoopAsync(ct), ct);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[DatalogicBarcodeScanner] Serial port {portName} open @ {_baudRate} baud. Listening for 1D Barcodes & 2D QR codes... ✓");
            Console.ResetColor();
            return true;
        }
        catch
        {
            _port?.Dispose();
            _port = null;
            return false;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        if (_port?.IsOpen == true)
            _port.Close();

        _port?.Dispose();
        _port = null;

        Console.WriteLine($"[DatalogicBarcodeScanner] Disconnected from {_comPort}.");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_port != null && _port.IsOpen && _port.BytesToRead > 0)
                {
                    string incoming = _port.ReadExisting();
                    if (!string.IsNullOrEmpty(incoming))
                    {
                        buffer.Append(incoming);

                        if (buffer.ToString().Contains('\r') || buffer.ToString().Contains('\n'))
                        {
                            string full = buffer.ToString().Trim();
                            buffer.Clear();
                            if (!string.IsNullOrWhiteSpace(full))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"\n  📷 [HARDWARE SCANNER] Scanned Code: {full}");
                                Console.ResetColor();
                                OnBarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(full));
                            }
                        }
                    }
                }
                else
                {
                    // If buffer has content but no newline arrived within 120ms (e.g. raw QR code stream without suffix), flush it
                    if (buffer.Length > 0)
                    {
                        await Task.Delay(120, ct).ConfigureAwait(false);
                        if (_port?.BytesToRead == 0 && buffer.Length > 0)
                        {
                            string full = buffer.ToString().Trim();
                            buffer.Clear();
                            if (!string.IsNullOrWhiteSpace(full))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"\n  📷 [HARDWARE SCANNER] Scanned Code: {full}");
                                Console.ResetColor();
                                OnBarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(full));
                            }
                        }
                    }
                }

                await Task.Delay(20, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DatalogicBarcodeScanner] Read error: {ex.Message}");
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
    }

    public void SimulateBarcodeScanned(string barcode)
    {
        if (!string.IsNullOrWhiteSpace(barcode))
            OnBarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(barcode.Trim()));
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}