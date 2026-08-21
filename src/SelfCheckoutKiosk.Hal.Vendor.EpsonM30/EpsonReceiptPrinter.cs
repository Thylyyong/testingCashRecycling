using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

/// <summary>
/// Line item snapshot for itemized supermarket receipt printing.
/// </summary>
public sealed record ReceiptLineItem
{
    public required string Name { get; init; }
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; } = 1;
    public decimal UnitPrice { get; init; }
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// HAL adapter for Epson and ESC/POS-compatible thermal receipt printers (EU-m30, TM-m30, TM-T88, POS-80, etc.).
///
/// Supported connection modes:
/// 1. Windows Print Spooler RAW mode (Default on Windows):
///    Sends unmodified ESC/POS byte streams directly to the targeted printer queue (e.g. "EPSON EU-m30")
///    via Win32 <c>winspool.drv</c> (<c>OpenPrinter</c> / <c>WritePrinter</c>).
///    Does NOT route through Windows GDI graphics rasterization and NEVER touches the default laptop printer.
///
/// 2. Serial / COM mode (e.g. "COM3", "COM6"):
///    Writes raw ESC/POS bytes directly to the COM port.
///
/// 3. Direct Port / Device Path (e.g. "/dev/usb/lp0" on Linux):
///    Writes directly via <see cref="FileStream"/>.
///
/// 4. Auto-detection ("AUTO"):
///    Scans installed system printers, identifies POS receipt printers, and automatically binds.
/// </summary>
public sealed class EpsonReceiptPrinter : IReceiptPrinter
{
    private bool _isConnected;
    private readonly string _targetSpecifier;

    private string _resolvedPrinterName = string.Empty;
    private string _resolvedPort = string.Empty;
    private string _driverName = string.Empty;
    private ConnectionMode _mode = ConnectionMode.Auto;

    private enum ConnectionMode
    {
        Auto,
        WinSpool,
        SerialCom,
        DirectFile
    }

    /// <summary>True once <see cref="ConnectAsync"/> has verified the printer is reachable.</summary>
    public bool IsConnected => _isConnected;

    public string PrinterName => !string.IsNullOrEmpty(_resolvedPrinterName) ? _resolvedPrinterName : _targetSpecifier;
    public string PortName => _resolvedPort;
    public string DriverName => _driverName;

    public event EventHandler<PrintJobStatusEventArgs>? OnJobStatusChanged;

    // ── ESC/POS constants ──────────────────────────────────────────────────
    private static readonly byte[] EscPosInit = {
        0x1B, 0x40,        // ESC @ : Initialize printer
        0x1C, 0x2E,        // FS .  : Cancel Chinese / Kanji mode (disables Asian double-byte mode)
        0x1B, 0x74, 0x00,  // ESC t 0 : Select Character Code Table 0 (PC437 Standard ASCII)
        0x1B, 0x61, 0x00   // ESC a 0 : Align left
    };
    private static readonly byte[] EscPosAlignLeft = { 0x1B, 0x61, 0x00 };      // Align left
    private static readonly byte[] EscPosFeedCut   = { 0x0A, 0x0A, 0x0A,       // Feed 3 lines
                                                       0x1D, 0x56, 0x42, 0x00 }; // Full cut

    /// <param name="target">
    ///   Printer identifier or connection mode:
    ///   - "AUTO" or null: Auto-detects the best POS receipt printer on the system.
    ///   - Printer Name (e.g. "EPSON EU-m30", "EPSON TM-m30 Receipt", "Generic / Text Only")
    ///   - "SPOOL:PrinterName": Explicit Windows Spooler queue name
    ///   - "COM3", "COM6": Serial port
    ///   - "USB001": USB port identifier (auto-maps to the printer queue on that port)
    /// </param>
    public EpsonReceiptPrinter(string target = "AUTO")
    {
        _targetSpecifier = string.IsNullOrWhiteSpace(target) ? "AUTO" : target.Trim();
    }

    // -----------------------------------------------------------------------
    // Core IReceiptPrinter Implementation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Performs a real-time health probe of the printer queue and physical hardware connection.
    /// </summary>
    public (bool IsOnline, string Reason) CheckHealth()
    {
        if (string.IsNullOrEmpty(_resolvedPrinterName))
        {
            ResolveTarget();
        }

        switch (_mode)
        {
            case ConnectionMode.WinSpool:
                var query = WinSpoolRawPrinter.QueryPrinterStatus(_resolvedPrinterName, _resolvedPort);
                _isConnected = query.IsOnline;
                return query;

            case ConnectionMode.SerialCom:
                bool comOk = !string.IsNullOrEmpty(_resolvedPort) && WinSpoolRawPrinter.SerialPortExists(_resolvedPort);
                _isConnected = comOk;
                return (comOk, comOk ? $"Serial POS ({_resolvedPort}) Online" : $"Serial Port {_resolvedPort} Disconnected");

            case ConnectionMode.DirectFile:
                bool fileOk = File.Exists(_resolvedPort);
                _isConnected = fileOk;
                return (fileOk, fileOk ? "Device File Ready" : "Device File Unavailable");

            default:
                _isConnected = false;
                return (false, "Unknown connection mode");
        }
    }

    /// <summary>
    /// Resolves the printer target, checks connectivity, and sends an ESC/POS Init command.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ResolveTarget();

            var health = CheckHealth();
            if (!health.IsOnline)
            {
                _isConnected = false;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[ReceiptPrinter] Printer '{_resolvedPrinterName}' ({_resolvedPort}) offline: {health.Reason}");
                Console.ResetColor();
                return;
            }

            bool success = false;
            switch (_mode)
            {
                case ConnectionMode.WinSpool:
                    success = WinSpoolRawPrinter.SendRawBytes(_resolvedPrinterName, EscPosInit, "Kiosk POS Init");
                    break;

                case ConnectionMode.SerialCom:
                case ConnectionMode.DirectFile:
                    success = await TryWriteDirectBytesAsync(_resolvedPort, EscPosInit, cancellationToken).ConfigureAwait(false);
                    break;
            }

            _isConnected = success;

            if (_isConnected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ReceiptPrinter] Connected to '{_resolvedPrinterName}' on {_resolvedPort} ({_driverName}). ✓");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[ReceiptPrinter] Printer '{_resolvedPrinterName}' ({_resolvedPort}) was not accessible.");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            _isConnected = false;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ReceiptPrinter] Connect error on target '{_targetSpecifier}': {ex.Message}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Checks readiness / paper sensor.
    /// </summary>
    public Task<bool> IsPaperPresentAsync(CancellationToken cancellationToken = default)
    {
        var health = CheckHealth();
        return Task.FromResult(health.IsOnline);
    }

    /// <summary>
    /// Sends a raw ESC/POS byte stream to the receipt printer and manages job status.
    /// Also creates a local audit copy for record keeping.
    /// </summary>
    public async Task PrintRawAsync(ReadOnlyMemory<byte> escPosPayload,
                                    CancellationToken cancellationToken = default)
    {
        if (escPosPayload.IsEmpty)
        {
            OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Failed));
            return;
        }

        OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Printing));

        byte[] payloadBytes = escPosPayload.ToArray();
        byte[] fullPayload = ConcatArrays(payloadBytes, EscPosFeedCut);
        bool printed = false;
        Exception? printException = null;

        try
        {
            if (!_isConnected || string.IsNullOrEmpty(_resolvedPrinterName))
            {
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_isConnected)
            {
                switch (_mode)
                {
                    case ConnectionMode.WinSpool:
                        printed = WinSpoolRawPrinter.SendRawBytes(_resolvedPrinterName, fullPayload, "Kiosk Transaction Receipt");
                        break;

                    case ConnectionMode.SerialCom:
                    case ConnectionMode.DirectFile:
                        printed = await TryWriteDirectBytesAsync(_resolvedPort, fullPayload, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            printException = ex;
            printed = false;
        }

        await SpoolAuditCopyAsync(escPosPayload, cancellationToken).ConfigureAwait(false);

        if (printed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[ReceiptPrinter 🧾] RECEIPT PRINTED SUCCESSFULLY ({escPosPayload.Length} bytes) to '{_resolvedPrinterName}'.");
            Console.ResetColor();
            OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Completed));
        }
        else
        {
            string err = printException?.Message ?? "Device unreachable or spooler error";
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[ReceiptPrinter ⚠️] Print failed: {err}. Audit copy recorded.");
            Console.ResetColor();
            OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Failed));
        }
    }

    /// <summary>
    /// Formats a complete supermarket transaction receipt with ESC/POS styling and prints it.
    /// Uses 48-column frame with balanced 2-space side padding (44-column content box) for standard 80mm thermal paper.
    /// Completely removes change due, suppresses Chinese/Kanji double-byte mode, and ensures strict ASCII alignment.
    /// </summary>
    public async Task PrintReceiptAsync(decimal totalUsd, decimal tenderedUsd,
                                        decimal overpaymentKhr,
                                        string? paymentMethod = "CASH",
                                        string? transactionId = null,
                                        int paperWidthCols = 48,
                                        decimal exchangeRate = 4100m,
                                        System.Collections.Generic.IEnumerable<ReceiptLineItem>? items = null,
                                        string? storeName = "SUPERMARKET EXPRESS",
                                        string? storeTagline = "AUTHENTIC FOOD & GROCERY",
                                        string? storeAddress = "Phnom Penh, Cambodia",
                                        string? storePhone = "+855 23 999 888",
                                        string? cashierName = "Kiosk #01",
                                        CancellationToken cancellationToken = default)
    {
        // 80mm thermal paper (79.5 +/- 0.5 mm): 576 dots / 12 dots-per-char = 48 columns edge-to-edge
        int width = paperWidthCols >= 30 ? paperWidthCols : 48;

        string divider = new string('-', width);
        string doubleDivider = new string('=', width);
        string txnId = !string.IsNullOrWhiteSpace(transactionId) ? transactionId : Guid.NewGuid().ToString()[..8].ToUpperInvariant();
        decimal rate = exchangeRate > 0 ? exchangeRate : 4100m;
        decimal totalKhr = Math.Ceiling((totalUsd * rate) / 100m) * 100m;

        var sb = new StringBuilder();

        // 1. Header (Centered across full 46-col width)
        sb.AppendLine(FormatCenter(SanitizeAscii(storeName ?? "SUPERMARKET EXPRESS"), width));
        if (!string.IsNullOrWhiteSpace(storeTagline))
        {
            sb.AppendLine(FormatCenter(SanitizeAscii(storeTagline), width));
        }
        if (!string.IsNullOrWhiteSpace(storeAddress))
        {
            sb.AppendLine(FormatCenter(SanitizeAscii(storeAddress), width));
        }
        if (!string.IsNullOrWhiteSpace(storePhone))
        {
            sb.AppendLine(FormatCenter($"Tel: {SanitizeAscii(storePhone)}", width));
        }
        sb.AppendLine(doubleDivider);

        // 2. Transaction Info
        sb.AppendLine(FormatRow($"Receipt: {txnId}", "Type: KIOSK_POS", width));
        sb.AppendLine(FormatRow($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}", $"Cashier: {SanitizeAscii(cashierName ?? "Kiosk #01")}", width));
        sb.AppendLine(divider);

        // 3. Itemized Header
        sb.AppendLine(FormatRow("QTY & ITEM", "AMOUNT", width));
        sb.AppendLine(divider);

        // 4. Purchased Items Breakdown
        var itemList = items != null ? items.ToList() : new System.Collections.Generic.List<ReceiptLineItem>();
        int totalItemCount = 0;
        int totalQtyCount = 0;

        if (itemList.Count > 0)
        {
            foreach (var item in itemList)
            {
                totalItemCount++;
                totalQtyCount += Math.Max(1, item.Quantity);

                // Line 1: Item Name (Sanitized pure ASCII)
                string itemName = SanitizeAscii(item.Name.Trim());
                if (itemName.Length > width) itemName = itemName.Substring(0, width - 1);
                sb.AppendLine(itemName);

                // Line 2: Sku / Qty x Price on left, Line Total on right (indented 2 spaces)
                string skuPrefix = !string.IsNullOrWhiteSpace(item.Sku) ? $"{SanitizeAscii(item.Sku)} " : "";
                string calcStr = $"{skuPrefix}{item.Quantity}x ${item.UnitPrice:F2}";
                string lineTotalStr = $"${item.LineTotal:F2}";
                sb.AppendLine("  " + FormatRow(calcStr, lineTotalStr, width - 2));
            }
        }
        else
        {
            totalItemCount = 1;
            totalQtyCount = 1;
            sb.AppendLine("1x Grocery Basket Total");
            sb.AppendLine("  " + FormatRow($"1x ${totalUsd:F2}", $"${totalUsd:F2}", width - 2));
        }

        sb.AppendLine(divider);
        sb.AppendLine(FormatRow($"TOTAL ITEMS: {totalItemCount}", $"({totalQtyCount} pcs)", width));
        sb.AppendLine(divider);

        // 5. Financial Breakdown (No tax, no change due)
        sb.AppendLine(FormatRow("TOTAL DUE (USD):", $"${totalUsd:F2}", width));
        sb.AppendLine(FormatRow("TOTAL DUE (KHR):", $"{totalKhr:N0} KHR", width));
        sb.AppendLine(divider);

        string methodStr = SanitizeAscii(paymentMethod?.ToUpperInvariant() ?? "CASH");
        decimal paidAmount = tenderedUsd > 0 ? tenderedUsd : totalUsd;
        sb.AppendLine(FormatRow($"PAID ({methodStr}):", $"${paidAmount:F2}", width));

        sb.AppendLine(doubleDivider);

        // 6. Payment Metadata
        sb.AppendLine(FormatRow("Payment Method:", methodStr, width));
        sb.AppendLine(FormatRow("Exchange Rate:", $"1 USD = {rate:N0} KHR", width));
        sb.AppendLine(FormatRow("Payment Status:", "PAID & COMPLETED", width));
        sb.AppendLine(divider);

        // 7. Footer
        sb.AppendLine(FormatCenter("THANK YOU FOR SHOPPING WITH US!", width));
        sb.AppendLine(FormatCenter("PLEASE KEEP THIS RECEIPT", width));
        sb.AppendLine(FormatCenter("HAVE A WONDERFUL DAY!", width));
        sb.AppendLine(doubleDivider);
        sb.AppendLine();
        sb.AppendLine();

        // Convert string using strict ASCII encoding to prevent any Chinese double-byte glyphs
        byte[] payload = Encoding.ASCII.GetBytes(sb.ToString());
        byte[] fullCmd = ConcatArrays(EscPosInit, payload);
        await PrintRawAsync(fullCmd, cancellationToken).ConfigureAwait(false);
    }

    private static string SanitizeAscii(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 32 && c <= 126)
            {
                sb.Append(c);
            }
            else if (c == '\n' || c == '\r' || c == '\t')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append(' ');
            }
        }
        return sb.ToString();
    }

    private static string FormatRow(string left, string right, int width = 48)
    {
        left ??= string.Empty;
        right ??= string.Empty;

        if (left.Length + right.Length >= width)
        {
            int maxLeft = Math.Max(1, width - right.Length - 1);
            if (left.Length > maxLeft)
            {
                left = left.Substring(0, maxLeft);
            }
        }

        int spaces = Math.Max(1, width - left.Length - right.Length);
        return left + new string(' ', spaces) + right;
    }

    private static string FormatCenter(string text, int width = 48)
    {
        text ??= string.Empty;
        if (text.Length >= width) return text.Substring(0, width);
        int leftPad = Math.Max(0, (width - text.Length) / 2);
        return new string(' ', leftPad) + text;
    }

    // -----------------------------------------------------------------------
    // Internal Resolution & Communication Helpers
    // -----------------------------------------------------------------------

    private void ResolveTarget()
    {
        string raw = _targetSpecifier;

        // Explicit "SPOOL:PrinterName"
        if (raw.StartsWith("SPOOL:", StringComparison.OrdinalIgnoreCase))
        {
            _mode = ConnectionMode.WinSpool;
            _resolvedPrinterName = raw.Substring(6).Trim();
            _resolvedPort = "WinSpool";
            return;
        }

        // Explicit COM port (e.g. "COM3", "COM6")
        if (raw.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && raw.Length >= 4 && char.IsDigit(raw[3]))
        {
            _mode = ConnectionMode.SerialCom;
            _resolvedPrinterName = $"Serial POS ({raw})";
            _resolvedPort = raw.ToUpperInvariant();
            return;
        }

        // AUTO or explicit name/port
        var best = PrinterDiscovery.FindBestReceiptPrinter(
            preferredName: raw.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ? null : raw,
            preferredPort: raw.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ? raw : null);

        if (best != null)
        {
            _mode = ConnectionMode.WinSpool;
            _resolvedPrinterName = best.Name;
            _resolvedPort = best.Port;
            _driverName = best.Driver;
            return;
        }

        // Direct device path on Linux (e.g. "/dev/usb/lp0")
        if (raw.StartsWith("/") || (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(raw)))
        {
            _mode = ConnectionMode.DirectFile;
            _resolvedPrinterName = raw;
            _resolvedPort = raw;
            return;
        }

        // Fallback: assume raw target is a Windows printer name
        _mode = ConnectionMode.WinSpool;
        _resolvedPrinterName = raw;
        _resolvedPort = "Unknown";
    }

    private static async Task<bool> TryWriteDirectBytesAsync(string portOrPath, byte[] bytes, CancellationToken ct)
    {
        try
        {
            string normalPath = NormalisePortPath(portOrPath);
            await using var stream = new FileStream(normalPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalisePortPath(string raw)
    {
        if (raw.StartsWith(@"\\", StringComparison.Ordinal) || raw.StartsWith("/", StringComparison.Ordinal))
            return raw;

        if (raw.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            return $@"\\.\{raw}";

        return $@"\\.\{raw}";
    }

    private static async Task SpoolAuditCopyAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SelfCheckoutKiosk", "receipts");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"receipt_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
            string text = Encoding.UTF8.GetString(payload.Span)
                                        .Replace("\x1B", "").Replace("\x1D", "");
            await File.WriteAllTextAsync(file, text, ct).ConfigureAwait(false);
        }
        catch { }
    }

    private static byte[] ConcatArrays(params byte[][] arrays)
    {
        int totalLength = arrays.Sum(a => a.Length);
        byte[] result = new byte[totalLength];
        int offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }
}
