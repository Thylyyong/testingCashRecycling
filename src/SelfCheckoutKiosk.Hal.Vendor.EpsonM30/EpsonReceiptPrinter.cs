using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

/// <summary>
/// HAL adapter for the Epson TM-m30 / EU-m30 thermal receipt printer.
/// Uses Windows spooler (winspool.drv) with explicit 64-bit Unicode RAW P/Invoke
/// for Windows 11 IoT / Desktop compatibility, falling back to direct COM port writes.
/// </summary>
public sealed class EpsonReceiptPrinter : IReceiptPrinter
{
    private bool _isConnected;
    private readonly string _printerPortPath;

    // ── winspool.drv 64-bit Unicode P/Invoke ─────────────────────────────────
    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPWStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 di);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBuf, int cbBuf, out int pcWritten);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    // EnumPrinters — used to discover all Windows printer queues
    [DllImport("winspool.Drv", EntryPoint = "EnumPrintersW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool EnumPrinters(
        int Flags,
        [MarshalAs(UnmanagedType.LPWStr)] string? Name,
        uint Level,
        IntPtr pPrinterEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    private const int PRINTER_ENUM_LOCAL      = 0x00000002;
    private const int PRINTER_ENUM_CONNECTIONS = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        public string? pServerName;
        public string? pPrinterName;
        public string? pShareName;
        public string? pPortName;
        public string? pDriverName;
        public string? pComment;
        public string? pLocation;
        public IntPtr pDevMode;
        public string? pSepFile;
        public string? pPrintProcessor;
        public string? pDatatype;
        public string? pParameters;
        public IntPtr pSecurityDescriptor;
        public uint   Attributes;
        public uint   Priority;
        public uint   DefaultPriority;
        public uint   StartTime;
        public uint   UntilTime;
        public uint   Status;
        public uint   cJobs;
        public uint   AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDatatype;
    }

    // ── Public surface ───────────────────────────────────────────────────────
    public bool IsConnected => _isConnected;
    public event EventHandler<PrintJobStatusEventArgs>? OnJobStatusChanged;

    private static readonly byte[] EscPosInit      = { 0x1B, 0x40 };
    private static readonly byte[] EscPosAlignLeft = { 0x1B, 0x61, 0x00 };
    private static readonly byte[] EscPosFeedCut   = { 0x0A, 0x0A, 0x0A, 0x1D, 0x56, 0x42, 0x00 };

    public EpsonReceiptPrinter(string printerPortPath = "EPSON EU-m30")
    {
        _printerPortPath = printerPortPath;
    }

    // ── ConnectAsync — REAL handshake ────────────────────────────────────────
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_printerPortPath.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsComPortPresent(_printerPortPath))
                throw new PrinterNotFoundException(
                    _printerPortPath,
                    $"COM port '{_printerPortPath}' was not found. Available ports: " +
                    string.Join(", ", GetAvailableComPorts()));
        }
        else
        {
            bool opened = OpenPrinter(_printerPortPath, out IntPtr hPrinter, IntPtr.Zero);
            if (!opened || hPrinter == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                var available = GetInstalledPrinters();
                string hint = available.Count == 0
                    ? "No printers found in Windows. Install the Epson TM-m30 driver first."
                    : $"Available Windows printers:\n" +
                      string.Join("\n", available.ConvertAll(p => $"    • {p}"));

                throw new PrinterNotFoundException(
                    _printerPortPath,
                    $"Printer '{_printerPortPath}' not found in Windows spooler (Win32 error {err}).\n{hint}");
            }
            ClosePrinter(hPrinter);
        }

        _isConnected = true;
        await Task.CompletedTask;
    }

    public async Task<bool> IsPaperPresentAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_isConnected);
    }

    public async Task PrintRawAsync(ReadOnlyMemory<byte> escPosPayload, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Call ConnectAsync() before printing.");

        OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Printing));

        try
        {
            if (_printerPortPath.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                await WriteToComPortAsync(escPosPayload, cancellationToken);
                await WriteToComPortAsync(EscPosFeedCut, cancellationToken);
            }
            else
            {
                await WriteToSpoolerAsync(escPosPayload, cancellationToken);
                await WriteToSpoolerAsync(EscPosFeedCut, cancellationToken);
            }

            await SpoolAuditCopyAsync(escPosPayload, cancellationToken);
            OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Completed));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EpsonReceiptPrinter] Print error: {ex.Message}");
            OnJobStatusChanged?.Invoke(this, new PrintJobStatusEventArgs(PrintJobState.Failed));
            throw;
        }
    }

    public async Task PrintReceiptAsync(decimal totalUsd, decimal tenderedUsd, decimal overpaymentKhr, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.Append("\x1B\x21\x30").AppendLine("SELF-CHECKOUT KIOSK").Append("\x1B\x21\x00");
        sb.AppendLine("Phnom Penh, Cambodia").AppendLine("========================================");
        sb.AppendLine($"Date   : {DateTime.Now:yyyy-MM-dd  HH:mm:ss}");
        sb.AppendLine($"Receipt: {Guid.NewGuid().ToString()[..8].ToUpper()}");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"Due    : ${totalUsd:F2} USD  ({(totalUsd * 4100m):N0} KHR)");
        sb.AppendLine($"Paid   : ${tenderedUsd:F2} USD");
        sb.AppendLine($"Change : {overpaymentKhr:N0} KHR");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("      THANK YOU FOR SHOPPING!");
        sb.AppendLine("========================================");

        byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] full = EscPosInit.Concat(EscPosAlignLeft).Concat(payload);
        await PrintRawAsync(full, cancellationToken);
    }

    public static List<string> GetInstalledPrinters()
    {
        var result = new List<string>();
        try
        {
            uint needed  = 0;
            uint returned = 0;
            int  flags   = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;

            EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out needed, out returned);
            if (needed == 0) return result;

            IntPtr buf = Marshal.AllocHGlobal((int)needed);
            try
            {
                bool ok = EnumPrinters(flags, null, 2, buf, needed, out needed, out returned);
                if (!ok) return result;

                int structSize = Marshal.SizeOf<PrinterInfo2>();
                for (int i = 0; i < (int)returned; i++)
                {
                    IntPtr ptr = IntPtr.Add(buf, i * structSize);
                    var info = Marshal.PtrToStructure<PrinterInfo2>(ptr);
                    if (!string.IsNullOrEmpty(info.pPrinterName))
                        result.Add($"{info.pPrinterName} [Port: {info.pPortName ?? "N/A"}, Driver: {info.pDriverName ?? "N/A"}]");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        catch
        {
        }
        return result;
    }

    private static bool IsComPortPresent(string portName)
    {
        try
        {
            var ports = System.IO.Ports.SerialPort.GetPortNames();
            return Array.Exists(ports, p => p.Equals(portName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private async Task WriteToSpoolerAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            if (!OpenPrinter(_printerPortPath, out var hPrinter, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Exception($"OpenPrinter failed for '{_printerPortPath}' (Win32 error {err}).");
            }

            try
            {
                var di = new DOC_INFO_1
                {
                    pDocName  = "Kiosk Receipt",
                    pOutputFile = null,
                    pDatatype = "RAW"
                };

                if (!StartDocPrinter(hPrinter, 1, ref di))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new Exception($"StartDocPrinter failed (Win32 error {err}).");
                }

                if (!StartPagePrinter(hPrinter))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new Exception($"StartPagePrinter failed (Win32 error {err}).");
                }

                IntPtr pBytes = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data.ToArray(), 0, pBytes, data.Length);
                    if (!WritePrinter(hPrinter, pBytes, data.Length, out int written))
                    {
                        int err = Marshal.GetLastWin32Error();
                        throw new Exception($"WritePrinter failed (Win32 error {err}, wrote {written}/{data.Length} bytes).");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pBytes);
                }

                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }, ct);
    }

    private async Task WriteToComPortAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            using var port = new System.IO.Ports.SerialPort(_printerPortPath, 9600, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One)
            {
                DtrEnable = true,
                RtsEnable = true,
                WriteTimeout = 3000
            };
            port.Open();
            byte[] bytes = data.ToArray();
            port.Write(bytes, 0, bytes.Length);
            port.Close();
        }, ct);
    }

    public static List<string> GetAvailableComPorts()
    {
        try
        {
            return new List<string>(System.IO.Ports.SerialPort.GetPortNames());
        }
        catch
        {
            return new List<string>();
        }
    }

    private static async Task<string?> SpoolAuditCopyAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SelfCheckoutKiosk", "receipts");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"receipt_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
            string text = Encoding.UTF8.GetString(payload.Span)
                .Replace("\x1B", "").Replace("\x1D", "").Replace("!", "");
            await File.WriteAllTextAsync(file, text, ct);
            return file;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[EpsonReceiptPrinter] Audit spool warning: {ex.Message}");
            return null;
        }
    }
}

public sealed class PrinterNotFoundException : Exception
{
    public string PrinterName { get; }
    public PrinterNotFoundException(string printerName, string message)
        : base(message) => PrinterName = printerName;
}

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
