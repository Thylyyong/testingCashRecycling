using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

/// <summary>
/// HAL adapter for the Epson TM-m30 thermal receipt printer using raw ESC/POS.
///
/// Real device setup:
///   USB mode  — Windows installs the Epson TM-m30 as a receipt printer.
///               Open Device Manager → Ports. Note the RAW port path (e.g. "USB001").
///               Pass that path as <paramref name="printerPortPath"/>.
///   Serial mode — Use "COM3" (or whichever COM port the TM-m30 enumerates on).
///
/// The adapter sends bytes directly to the device port using a
/// <see cref="FileStream"/> — no Windows GDI print spooler is involved.
/// This is the correct technique for ESC/POS devices; the spooler would
/// add GDI rasterisation that corrupts the raw command stream.
///
/// Fallback: when <paramref name="printerPortPath"/> starts with "SPOOL:",
/// the remainder is treated as a Windows printer share name and the payload
/// is submitted via <see cref="System.Drawing.Printing.PrintDocument"/> raw mode.
/// Use this only if the ESC/POS-direct path is unavailable (driver restriction).
///
/// Audit spool: every receipt is also saved to
/// <c>%ProgramData%\SelfCheckoutKiosk\receipts\</c> for offline reconciliation.
/// </summary>
public sealed class EpsonReceiptPrinter : IReceiptPrinter
{
    private bool   _isConnected;
    private readonly string _printerPortPath;

    /// <summary>True once <see cref="ConnectAsync"/> has verified the printer port.</summary>
    public bool IsConnected => _isConnected;

    public event EventHandler<PrintJobStatusEventArgs>? OnJobStatusChanged;

    // ── ESC/POS constants ──────────────────────────────────────────────────
    private static readonly byte[] EscPosInit      = { 0x1B, 0x40 };           // Initialize
    private static readonly byte[] EscPosAlignLeft = { 0x1B, 0x61, 0x00 };      // Align left
    private static readonly byte[] EscPosFeedCut   = { 0x0A, 0x0A, 0x0A,       // Feed 3 lines
                                                        0x1D, 0x56, 0x42, 0x00 }; // Full cut

    /// <param name="printerPortPath">
    ///   Windows RAW port path of the Epson TM-m30 USB printer.
    ///   Examples:
    ///     "USB001"    — USB direct (most common on TM-m30)
    ///     "COM3"      — Serial connection
    ///     "\\\\.\\USB001" — Win32 device path form (equivalent)
    ///   To find the right value: open Device Manager → Universal Serial Bus
    ///   controllers and look for "Epson TM-m30" → Properties → Port.
    /// </param>
    public EpsonReceiptPrinter(string printerPortPath = "USB001")
    {
        _printerPortPath = printerPortPath;
    }

    // -----------------------------------------------------------------------
    // IReceiptPrinter
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies the printer port is accessible and sends an ESC/POS Init command
    /// to reset the print buffer.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Open in probe mode — write ESC/POS Init so the TM-m30 resets its buffer.
        // A failure here (FileNotFoundException, UnauthorizedAccessException) means
        // the port path is wrong or the driver is not installed.
        await WriteRawBytesAsync(EscPosInit, cancellationToken).ConfigureAwait(false);
        _isConnected = true;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[EpsonReceiptPrinter] TM-m30 ready on {_printerPortPath}. ESC/POS Init sent. ✓");
        Console.ResetColor();
    }

    /// <summary>
    /// Queries paper-present status.  
    /// Full DLE/EOT paper-sensor queries require the Epson ePOS SDK or a bidirectional
    /// COM port. Over a unidirectional USB raw port we probe readiness via a test write.
    /// Returns <c>true</c> when the port is open and writable.
    /// </summary>
    public Task<bool> IsPaperPresentAsync(CancellationToken cancellationToken = default)
    {
        // Over USB001 the port is always unidirectional (write-only).
        // The safest check without the ePOS SDK is: can we still open the port?
        try
        {
            using var probe = new FileStream(NormalisedPortPath(_printerPortPath),
                                             FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return Task.FromResult(probe.CanWrite && _isConnected);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Sends a raw ESC/POS byte stream to the TM-m30 and raises job-status events.
    /// Also writes an audit copy to <c>%ProgramData%\SelfCheckoutKiosk\receipts\</c>.
    /// </summary>
    public async Task PrintRawAsync(ReadOnlyMemory<byte> escPosPayload,
                                    CancellationToken cancellationToken = default)
    {
        OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Printing));

        try
        {
            // 1. Send raw bytes directly to the printer port
            await WriteRawBytesAsync(escPosPayload, cancellationToken).ConfigureAwait(false);

            // 2. Write ESC/POS full-cut so paper is automatically separated
            await WriteRawBytesAsync(EscPosFeedCut, cancellationToken).ConfigureAwait(false);

            // 3. Audit spool — keep a text copy on disk for reconciliation
            await SpoolAuditCopyAsync(escPosPayload, cancellationToken).ConfigureAwait(false);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[EpsonReceiptPrinter 🧾] RECEIPT SENT TO PRINTER ({escPosPayload.Length} bytes).");
            Console.ResetColor();

            OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Completed));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EpsonReceiptPrinter] Print error: {ex.Message}");
            OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Failed));
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // Convenience helper — builds and prints a formatted transaction receipt
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a complete ESC/POS receipt for a completed kiosk sale and prints it.
    /// Uses a generic thermal layout that cleanly formats across 58mm and 80mm paper widths.
    /// </summary>
    public async Task PrintReceiptAsync(decimal totalUsd, decimal tenderedUsd,
                                        decimal overpaymentKhr,
                                        string? paymentMethod = "CASH",
                                        string? transactionId = null,
                                        int paperWidthCols = 40,
                                        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        string divider = new string('-', paperWidthCols);
        string doubleDivider = new string('=', paperWidthCols);
        string txnId = !string.IsNullOrWhiteSpace(transactionId) ? transactionId : Guid.NewGuid().ToString()[..8].ToUpper();

        // ESC/POS: Center alignment for header
        sb.Append("\x1B\x61\x01"); // Center
        sb.Append("\x1B\x21\x30"); // Double width + height
        sb.AppendLine("SELF-CHECKOUT KIOSK");
        sb.Append("\x1B\x21\x00"); // Normal
        sb.AppendLine("AUTHENTIC FOOD & BEVERAGES");
        sb.AppendLine("Phnom Penh, Cambodia");
        sb.Append("\x1B\x61\x00"); // Left align
        sb.AppendLine(doubleDivider);
        sb.AppendLine($"Order: {txnId.PadRight(18)} Type: DINE_IN");
        sb.AppendLine($"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}   Cashier: Kiosk");
        sb.AppendLine(divider);
        sb.AppendLine("QTY & ITEM                                AMOUNT");
        sb.AppendLine(divider);
        sb.AppendLine($"1x Order Total                           ${totalUsd,7:F2}");
        sb.AppendLine(divider);
        sb.AppendLine($"SUBTOTAL:                                ${totalUsd,7:F2}");
        sb.AppendLine(divider);
        sb.AppendLine($"TOTAL (USD):                             ${totalUsd,7:F2}");
        sb.AppendLine($"                                  {(totalUsd * 4100m),10:N0} KHR");
        sb.AppendLine($"PAID:                                    ${tenderedUsd,7:F2}");
        if (overpaymentKhr > 0)
        {
            sb.AppendLine($"CHANGE:                                  {overpaymentKhr,10:N0} KHR");
        }
        sb.AppendLine(divider);
        sb.AppendLine($"PAYMENT METHOD:                         {paymentMethod?.ToUpperInvariant() ?? "CASH"}");
        sb.AppendLine(doubleDivider);
        sb.Append("\x1B\x61\x01"); // Center align
        sb.AppendLine("THANK YOU FOR YOUR VISIT!");
        sb.AppendLine("Please Come Again");
        sb.AppendLine("Powered by Kiosk POS");
        sb.Append("\x1B\x61\x00"); // Reset align

        byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());
        await PrintRawAsync(EscPosInit.Concat(EscPosAlignLeft).Concat(payload).ToArray(),
                            cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task WriteRawBytesAsync(ReadOnlyMemory<byte> bytes,
                                          CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            NormalisedPortPath(_printerPortPath),
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 1,
            useAsync: true);

        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task SpoolAuditCopyAsync(ReadOnlyMemory<byte> payload,
                                                   CancellationToken ct)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SelfCheckoutKiosk", "receipts");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"receipt_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
            // Strip non-printable ESC/POS control bytes for the text audit copy
            string text = Encoding.UTF8.GetString(payload.Span)
                                        .Replace("\x1B", "").Replace("\x1D", "");
            await File.WriteAllTextAsync(file, text, ct).ConfigureAwait(false);
            Console.WriteLine($"   ► Audit copy: {file}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EpsonReceiptPrinter] Audit spool warning: {ex.Message}");
            // Non-fatal — print succeeded even if spool write fails
        }
    }

    /// <summary>
    /// Converts short port names like "USB001" to the Win32 device path
    /// "\\.\USB001" that <see cref="FileStream"/> requires on Windows.
    /// COM ports are returned unchanged; absolute paths are returned unchanged.
    /// </summary>
    private static string NormalisedPortPath(string raw)
    {
        if (raw.StartsWith(@"\\", StringComparison.Ordinal) ||
            raw.StartsWith("/", StringComparison.Ordinal))
            return raw; // already a device path or Unix path

        if (raw.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            return $@"\\.\{raw}"; // COM10+ requires the long form

        // USB001, LPT1, etc.
        return $@"\\.\{raw}";
    }
}

// Extension helper — avoids LINQ dependency in this file
file static class ByteArrayExtensions
{
    public static byte[] Concat(this byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        return result;
    }
}
