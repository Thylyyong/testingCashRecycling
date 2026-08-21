using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

/// <summary>
/// Low-level Win32 Print Spooler (winspool.drv) helper for sending raw byte payloads
/// (ESC/POS command streams) directly to a named Windows printer queue without GDI rasterisation.
/// Includes real-time status and physical connectivity probing.
/// </summary>
public static class WinSpoolRawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDocName = "Kiosk POS Receipt";
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pOutputFile = null;
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDataType = "RAW";
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    [DllImport("winspool.drv", EntryPoint = "GetPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool GetPrinter(IntPtr hPrinter, int dwLevel, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

    // Configuration Manager PnP API for instant USB hardware plug/unplug status
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int CM_Locate_DevNode(out IntPtr dnDevInst, string pDeviceID, int ulFlags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status", SetLastError = true)]
    private static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, IntPtr dnDevInst, int ulFlags);

    private const uint PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x00000400;
    private const uint PRINTER_STATUS_ERROR           = 0x00000002;
    private const uint PRINTER_STATUS_PAPER_JAM       = 0x00000008;
    private const uint PRINTER_STATUS_PAPER_OUT       = 0x00000010;
    private const uint PRINTER_STATUS_OFFLINE         = 0x00000080;
    private const uint PRINTER_STATUS_DOOR_OPEN       = 0x00400000;
    private const uint PRINTER_STATUS_NOT_AVAILABLE   = 0x00001000;

    /// <summary>
    /// Checks if a printer queue exists and can be opened in Windows.
    /// </summary>
    public static bool CanOpenPrinter(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return false;
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            if (OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
            {
                ClosePrinter(hPrinter);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Performs a real-time probe of the printer queue and underlying physical hardware.
    /// Returns true if the device is currently plugged in and ready to print.
    /// </summary>
    public static (bool IsOnline, string Reason) QueryPrinterStatus(string printerName, string portName = "")
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return (false, "No printer specified");

        if (!OperatingSystem.IsWindows())
            return (true, "Non-Windows environment");

        IntPtr hPrinter = IntPtr.Zero;
        if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
        {
            return (false, "Printer queue unavailable");
        }

        try
        {
            GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out int bytesNeeded);
            if (bytesNeeded > 0)
            {
                IntPtr pInfo = Marshal.AllocHGlobal(bytesNeeded);
                try
                {
                    if (GetPrinter(hPrinter, 2, pInfo, bytesNeeded, out _))
                    {
                        uint attributes = (uint)Marshal.ReadInt32(pInfo, 13 * IntPtr.Size);
                        uint status = (uint)Marshal.ReadInt32(pInfo, 13 * IntPtr.Size + 20);

                        if ((attributes & PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0)
                            return (false, "Printer set to Work Offline");

                        if ((status & PRINTER_STATUS_OFFLINE) != 0 || (status & PRINTER_STATUS_NOT_AVAILABLE) != 0)
                            return (false, "Printer is Offline / Powered Off");

                        if ((status & PRINTER_STATUS_DOOR_OPEN) != 0)
                            return (false, "Printer Cover / Door Open");

                        if ((status & PRINTER_STATUS_PAPER_OUT) != 0)
                            return (false, "Out of Paper");

                        if ((status & PRINTER_STATUS_PAPER_JAM) != 0)
                            return (false, "Paper Jam");

                        if ((status & PRINTER_STATUS_ERROR) != 0)
                            return (false, "Printer Error");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pInfo);
                }
            }

            // If the printer is connected to a USB or COM port, verify physical hardware presence
            if (!string.IsNullOrWhiteSpace(portName))
            {
                if (portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                {
                    bool usbPresent = IsUsbPrinterHardwarePresent(portName, printerName);
                    if (!usbPresent)
                    {
                        return (false, "USB Cable Unplugged / Disconnected");
                    }
                }
                else if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                {
                    bool comExists = SerialPortExists(portName);
                    if (!comExists)
                    {
                        return (false, $"Serial Port {portName} Disconnected");
                    }
                }
            }

            return (true, "Online & Ready");
        }
        catch (Exception ex)
        {
            return (false, $"Status error: {ex.Message}");
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    /// <summary>
    /// Verifies whether the physical USB printer hardware is attached to the system.
    /// </summary>
    private static bool IsUsbPrinterHardwarePresent(string portName, string printerName)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            // 1. Query PnP USB devices via cfgmgr32
            string[] knownDevIds =
            {
                @"USB\VID_04B8&PID_0E2E\58415A460025110000", // Epson EU-m30
                @"USBPRINT\EPSONEU-M30\7&1E3CE638&0&USB001"
            };

            foreach (var devId in knownDevIds)
            {
                IntPtr devInst = IntPtr.Zero;
                int cr = CM_Locate_DevNode(out devInst, devId, 0);
                if (cr == 0)
                {
                    uint pulStatus = 0;
                    uint problem = 0;
                    if (CM_Get_DevNode_Status(out pulStatus, out problem, devInst, 0) == 0)
                    {
                        if (problem == 0 && (pulStatus & 0x00000008) != 0) // DN_STARTED
                        {
                            return true;
                        }
                    }
                }
            }

            // 2. Fallback: check registry hardware device map
            using var usbHubKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\usbprint\Enum");
            if (usbHubKey != null)
            {
                int count = Convert.ToInt32(usbHubKey.GetValue("Count") ?? 0);
                if (count > 0)
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    public static bool SerialPortExists(string comPort)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key != null)
            {
                foreach (var val in key.GetValueNames())
                {
                    var name = key.GetValue(val)?.ToString();
                    if (string.Equals(name, comPort, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Sends a raw byte stream directly to the specified printer queue using Win32 Spooler RAW mode.
    /// </summary>
    public static bool SendRawBytes(string printerName, byte[] bytes, string docName = "Kiosk POS Receipt")
    {
        if (string.IsNullOrWhiteSpace(printerName) || bytes == null || bytes.Length == 0) return false;
        if (!OperatingSystem.IsWindows()) return false;

        IntPtr hPrinter = IntPtr.Zero;
        var di = new DOCINFOA
        {
            pDocName = docName,
            pDataType = "RAW"
        };

        if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
        {
            return false;
        }

        bool success = false;
        try
        {
            if (StartDocPrinter(hPrinter, 1, di))
            {
                try
                {
                    if (StartPagePrinter(hPrinter))
                    {
                        IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                        try
                        {
                            Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);
                            int dwWritten = 0;
                            success = WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out dwWritten) && (dwWritten == bytes.Length);
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(pUnmanagedBytes);
                            EndPagePrinter(hPrinter);
                        }
                    }
                }
                finally
                {
                    EndDocPrinter(hPrinter);
                }
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }

        return success;
    }
}
