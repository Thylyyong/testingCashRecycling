using System.IO.Ports;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

namespace SelfCheckoutKiosk.App;

/// <summary>Read-only workstation hardware discovery for the verification workflow.</summary>
internal static class HardwareDiscovery
{
    internal static async Task RunAsync()
    {
        Console.WriteLine("Hardware discovery (read-only; no device will be enabled or printed to)");
        Console.WriteLine();

        string[] ports = SerialPort.GetPortNames()
            .OrderBy(static port => port, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine("Serial ports detected:");
        Console.WriteLine(ports.Length == 0 ? "  None" : $"  {string.Join(", ", ports)}");

        await FindCashRecyclerAsync(ports);
        if (OperatingSystem.IsWindows())
            ListPrinterCandidates();
        else
            Console.WriteLine("\nReceipt printer candidates:\n  Windows printer discovery is unavailable on this operating system.");
        ListScannerCandidates(ports);
    }

    private static async Task FindCashRecyclerAsync(string[] ports)
    {
        Console.WriteLine();
        Console.WriteLine("Cash recycler:");
        try
        {
            string? apiKey = TryReadApiKey();
            var options = CashRecyclerOptions.FromEnvironment(apiKey);
            string? port = await CashRecyclerDiscovery.FindConnectedPortAsync(options, ports);
            Console.WriteLine(port is null
                ? "  No recycler was verified by the vendor REST API."
                : $"  Verified vendor REST API candidate: {port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Not checked: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ListPrinterCandidates()
    {
        Console.WriteLine();
        Console.WriteLine("Receipt printer candidates:");
        using var printersKey = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Print\Printers");
        var printers = (printersKey?.GetSubKeyNames() ?? [])
            .Where(static name => name.Contains("EPSON", StringComparison.OrdinalIgnoreCase) ||
                                  name.Contains("TM-M30", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Console.WriteLine(printers.Length == 0
            ? "  No Epson/TM-m30 Windows printer queue found."
            : string.Join(Environment.NewLine, printers.Select(static name => $"  {name}")));
    }

    private static void ListScannerCandidates(string[] ports)
    {
        Console.WriteLine();
        Console.WriteLine("Barcode scanner candidates:");
        Console.WriteLine(ports.Length == 0
            ? "  No USB-COM scanner candidate found. A HID keyboard-wedge scanner needs a test scan."
            : "  Any listed COM port may be a Datalogic USB-COM scanner; confirm with a test scan in verification mode.");
    }

    private static string? TryReadApiKey()
    {
        try { return File.ReadAllText("api_key.secret").Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
