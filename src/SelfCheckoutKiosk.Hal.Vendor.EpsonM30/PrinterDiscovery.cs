using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

/// <summary>
/// Information about an installed system printer.
/// </summary>
public sealed record DiscoveredPrinter
{
    public required string Name { get; init; }
    public string Driver { get; init; } = string.Empty;
    public string Port { get; init; } = string.Empty;
    public bool IsPosReceiptPrinter { get; init; }
    public int PriorityScore { get; init; }

    public override string ToString() => $"{Name} ({Port}) [{Driver}]";
}

/// <summary>
/// Auto-discovery and enumeration service for POS receipt and thermal printers.
/// </summary>
public static class PrinterDiscovery
{
    private static readonly string[] ExcludedKeywords =
    {
        "pdf", "fax", "onenote", "xps", "root print queue", "document writer", "send to", "virtual"
    };

    private static readonly (string Keyword, int Weight)[] PosPrinterKeywords =
    {
        ("eu-m30", 100),
        ("tm-m30", 100),
        ("tm-t88", 90),
        ("tm-t20", 90),
        ("tm-t82", 90),
        ("epson tm", 85),
        ("epson eu", 85),
        ("generic / text only", 80),
        ("generic", 70),
        ("pos-80", 80),
        ("pos-58", 80),
        ("pos", 75),
        ("thermal", 75),
        ("receipt", 75),
        ("ticket", 70),
        ("star tsp", 80),
        ("bixolon", 80),
        ("xprinter", 80),
        ("citizen", 80),
        ("rongta", 75),
        ("sewoo", 75),
        ("snbc", 75),
        ("custom", 60),
        ("zebra", 50)
    };

    /// <summary>
    /// Enumerates all installed printers on the system and identifies POS receipt printers.
    /// </summary>
    public static IReadOnlyList<DiscoveredPrinter> GetInstalledPrinters()
    {
        var result = new List<DiscoveredPrinter>();

        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        try
        {
            // 1. Read from HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Print\Printers
            using var printersKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Print\Printers");
            if (printersKey != null)
            {
                foreach (var subKeyName in printersKey.GetSubKeyNames())
                {
                    using var pKey = printersKey.OpenSubKey(subKeyName);
                    if (pKey == null) continue;

                    string name = subKeyName;
                    string driver = pKey.GetValue("Printer Driver")?.ToString() ?? string.Empty;
                    string port = pKey.GetValue("Port")?.ToString() ?? string.Empty;

                    var evaluated = EvaluatePrinter(name, driver, port);
                    result.Add(evaluated);
                }
            }
        }
        catch { }

        // 2. Fallback check HKCU Devices if HKLM was empty or restricted
        if (result.Count == 0)
        {
            try
            {
                using var devicesKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Devices");
                if (devicesKey != null)
                {
                    foreach (var valName in devicesKey.GetValueNames())
                    {
                        string val = devicesKey.GetValue(valName)?.ToString() ?? string.Empty;
                        // format is usually "winspool,Ne03:"
                        var evaluated = EvaluatePrinter(valName, string.Empty, val);
                        result.Add(evaluated);
                    }
                }
            }
            catch { }
        }

        return result.OrderByDescending(p => p.PriorityScore).ToList();
    }

    /// <summary>
    /// Finds the best available receipt printer based on configuration or auto-detection.
    /// </summary>
    public static DiscoveredPrinter? FindBestReceiptPrinter(string? preferredName = null, string? preferredPort = null)
    {
        var installed = GetInstalledPrinters();

        // 1. If explicit preferredName is given and not AUTO, look for exact or partial match
        if (!string.IsNullOrWhiteSpace(preferredName) && !preferredName.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            var match = installed.FirstOrDefault(p => p.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                     ?? installed.FirstOrDefault(p => p.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase));

            if (match != null) return match;

            // If not found in registry but WinSpool can open it, create entry
            if (WinSpoolRawPrinter.CanOpenPrinter(preferredName))
            {
                return new DiscoveredPrinter
                {
                    Name = preferredName,
                    Driver = "Configured Printer",
                    Port = preferredPort ?? "SPOOL",
                    IsPosReceiptPrinter = true,
                    PriorityScore = 100
                };
            }
        }

        // 2. If explicit preferredPort is given (e.g. "USB001" or "COM3")
        if (!string.IsNullOrWhiteSpace(preferredPort) && !preferredPort.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            var portMatch = installed.FirstOrDefault(p => p.Port.Equals(preferredPort, StringComparison.OrdinalIgnoreCase));
            if (portMatch != null) return portMatch;
        }

        // 3. Auto-pick highest scoring POS receipt printer
        var bestPos = installed.FirstOrDefault(p => p.IsPosReceiptPrinter && p.PriorityScore > 0);
        if (bestPos != null) return bestPos;

        // 4. Fallback: Any non-excluded printer on USB, COM, or Spool
        var fallbackPrinter = installed.FirstOrDefault(p => p.PriorityScore >= 0);
        if (fallbackPrinter != null) return fallbackPrinter;

        // 5. Last resort: Return first installed printer if any exists
        return installed.FirstOrDefault();
    }

    private static DiscoveredPrinter EvaluatePrinter(string name, string driver, string port)
    {
        string combined = $"{name} {driver}".ToLowerInvariant();

        // Check excluded keywords (PDF, Fax, OneNote, XPS, etc.)
        bool isExcluded = ExcludedKeywords.Any(k => combined.Contains(k));

        int score = 0;

        if (!isExcluded)
        {
            foreach (var (kw, weight) in PosPrinterKeywords)
            {
                if (combined.Contains(kw))
                {
                    score = Math.Max(score, weight);
                }
            }

            // Port heuristics
            string portLower = port.ToLowerInvariant();
            if (portLower.StartsWith("usb") || portLower.StartsWith("com") || portLower.StartsWith("lpt"))
            {
                score += 15;
            }
        }

        return new DiscoveredPrinter
        {
            Name = name,
            Driver = driver,
            Port = port,
            IsPosReceiptPrinter = !isExcluded && score >= 50,
            PriorityScore = isExcluded ? -100 : score
        };
    }
}
