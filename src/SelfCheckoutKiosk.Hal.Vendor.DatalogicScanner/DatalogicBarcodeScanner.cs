using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;

/// <summary>
/// HAL adapter for a Datalogic barcode scanner over USB-COM virtual serial port.
/// The scanner emulates a standard RS-232 COM port via its USB-COM driver.
///
/// Real device setup:
///   1. Install Datalogic USB-COM driver (creates e.g. COM4 in Device Manager).
///   2. Set <comPort> to match the assigned port (e.g. "COM4").
///   3. Baud rate 9600, 8N1 — Datalogic factory default.
///
/// If the scanner is in HID keyboard-wedge mode (no COM port visible):
///   ConnectAsync / DisconnectAsync become no-ops.
///   Barcodes arrive as keystroke events; read them via Console/WinUI TextInput.
///
/// Architecture rule: this adapter emits the raw barcode string only.
/// Classification (EAN/QR/PLU) is entirely the engine RegexRouter's responsibility.
/// </summary>
public sealed class DatalogicBarcodeScanner : IBarcodeScanner, IAsyncDisposable
{
    private SerialPort?          _port;
    private CancellationTokenSource? _cts;
    private readonly string      _comPort;
    private readonly int         _baudRate;

    /// <summary>True once <see cref="ConnectAsync"/> succeeds and the port is open.</summary>
    public bool IsConnected => _port?.IsOpen == true;

    public event EventHandler<BarcodeScannedEventArgs>? OnBarcodeScanned;

    /// <param name="comPort">
    ///   Windows COM port the Datalogic USB-COM driver assigned (e.g. "COM4").
    ///   Check Device Manager → Ports (COM &amp; LPT).
    /// </param>
    /// <param name="baudRate">
    ///   Scanner baud rate. Factory default is 9600; change only if reconfigured via Datalogic Aladdin.
    /// </param>
    public DatalogicBarcodeScanner(string comPort = "COM4", int baudRate = 9600)
    {
        _comPort  = comPort;
        _baudRate = baudRate;
    }

    /// <summary>
    /// Opens the COM port and starts listening for barcode data in the background.
    /// Fires <see cref="OnBarcodeScanned"/> for each complete scan line received.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_port?.IsOpen == true)
            return Task.CompletedTask; // already open

        _port = new SerialPort(_comPort, _baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout  = 500,  // ms — short so reads don't block forever
            WriteTimeout = 500,
            NewLine      = "\r"  // Datalogic default suffix; "\r\n" also works
        };

        _port.Open();

        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        // Background reader — one scan per line
        _ = Task.Run(() => ReadLoopAsync(ct), ct);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[DatalogicBarcodeScanner] Serial port {_comPort} open @ {_baudRate} baud. Listening for scans... ✓");
        Console.ResetColor();

        return Task.CompletedTask;
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

    // -----------------------------------------------------------------------
    // Background serial read loop
    // -----------------------------------------------------------------------
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // ReadLine blocks until \r (or \r\n) arrives from the scanner
                string raw = _port!.ReadLine();
                if (!string.IsNullOrWhiteSpace(raw))
                    OnBarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(raw.Trim()));
            }
            catch (TimeoutException)
            {
                // No data this window — normal; keep looping
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log hardware fault but keep loop alive so a momentary glitch
                // does not kill the background thread permanently.
                Console.Error.WriteLine($"[DatalogicBarcodeScanner] Read error: {ex.Message}");
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Test / verification helper — lets the hardware harness inject a scan
    // without physical hardware present.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Injects a barcode as if the physical scanner had read it.
    /// Used by <see cref="HardwareVerificationHarness"/> and unit tests only.
    /// </summary>
    public void SimulateBarcodeScanned(string barcode)
    {
        if (!string.IsNullOrWhiteSpace(barcode))
            OnBarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(barcode.Trim()));
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
