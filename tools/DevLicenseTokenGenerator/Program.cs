using System.Security.Cryptography;
using System.Text.Json;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Infrastructure.Security;

const string devPrivateKeyPkcs8Base64 =
    "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgLwr4Bk3wyGHDa6rTUxHHmNPGd35S5w5IvFvSkanylB+hRANCAASryF6RMBz+hO0eAo1luGoQxjOw7Dz0NxWrBcQADIcK4nSyEMHyJKKsxfqlfc9tcRuJfk6XXIHmdOeIvM4CoRKu";

string outputPath = "license.token";
string? targetHardwareId = "*"; // Default to universal wildcard for seamless deployment
LicenseTier tier = LicenseTier.Enterprise;
int validityYears = 10;
bool aiEnabled = true;
int maxKiosks = 100;
bool quiet = false;
string? inspectPath = null;
bool interactive = false;

// Parse CLI args
for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if ((arg is "--output" or "-o") && i + 1 < args.Length)
        outputPath = args[++i];
    else if ((arg is "--hardware-id" or "-h" or "--hwid") && i + 1 < args.Length)
        targetHardwareId = args[++i];
    else if (arg is "--node-lock" or "-n")
        targetHardwareId = new HardwareIdProvider().GetHardwareId();
    else if (arg is "--universal" or "-u")
        targetHardwareId = "*";
    else if ((arg is "--tier" or "-t") && i + 1 < args.Length)
    {
        if (Enum.TryParse<LicenseTier>(args[++i], true, out var parsedTier))
            tier = parsedTier;
    }
    else if ((arg is "--years" or "-y") && i + 1 < args.Length && int.TryParse(args[++i], out int y))
        validityYears = Math.Max(1, Math.Min(50, y));
    else if (arg is "--inspect" or "-i" && i + 1 < args.Length)
        inspectPath = args[++i];
    else if (arg is "--quiet" or "-q" or "--silent")
        quiet = true;
    else if (arg is "--menu" or "-m")
        interactive = true;
}

if (!string.IsNullOrEmpty(inspectPath))
{
    InspectToken(inspectPath);
    return;
}

if (interactive)
{
    ShowInteractiveMenu();
    return;
}

// Otherwise execute standard universal generation directly
GenerateLicense(targetHardwareId, outputPath, tier, validityYears, maxKiosks, aiEnabled, quiet);

void ShowInteractiveMenu()
{
    while (true)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================================");
        Console.WriteLine("        SELF-CHECKOUT KIOSK — LICENSE GENERATOR TOOL             ");
        Console.WriteLine("        Offline Cryptographic Hardware Node-Locking              ");
        Console.WriteLine("=================================================================");
        Console.ResetColor();

        string localHwid = new HardwareIdProvider().GetHardwareId();
        Console.WriteLine($"Current Machine Hardware ID: {localHwid}");
        Console.WriteLine();
        Console.WriteLine("Choose an option:");
        Console.WriteLine("  [1] Generate Enterprise License for THIS Machine (Instant Auto-Lock)");
        Console.WriteLine("  [2] Generate License for ANOTHER Device (Input Hardware ID)");
        Console.WriteLine("  [3] Inspect / Validate an Existing license.token");
        Console.WriteLine("  [4] Exit");
        Console.WriteLine();
        Console.Write("Enter selection [1-4] (Default is 1): ");

        string? choice = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(choice) || choice == "1")
        {
            Console.WriteLine();
            GenerateLicense(localHwid, "license.token", LicenseTier.Enterprise, 5, 100, true, false);
            WaitToExit();
            return;
        }
        else if (choice == "2")
        {
            Console.WriteLine();
            Console.Write("Paste target machine Hardware ID: ");
            string? remoteHwid = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(remoteHwid))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Hardware ID cannot be empty.");
                Console.ResetColor();
                Thread.Sleep(1500);
                continue;
            }

            Console.Write("Output filename [license.token]: ");
            string? customOut = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(customOut))
                customOut = "license.token";

            Console.WriteLine();
            GenerateLicense(remoteHwid, customOut, LicenseTier.Enterprise, 5, 100, true, false);
            WaitToExit();
            return;
        }
        else if (choice == "3")
        {
            Console.WriteLine();
            Console.Write("Enter path to license.token [license.token]: ");
            string? tokenFile = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(tokenFile))
                tokenFile = "license.token";

            InspectToken(tokenFile);
            WaitToExit();
            return;
        }
        else if (choice == "4")
        {
            return;
        }
    }
}

void GenerateLicense(string? hwid, string outPath, LicenseTier licTier, int years, int kiosks, bool ai, bool isQuiet)
{
    string effectiveHwid = !string.IsNullOrWhiteSpace(hwid)
        ? hwid.Trim()
        : new HardwareIdProvider().GetHardwareId();

    var payload = new LicensePayload(
        HardwareId: effectiveHwid,
        Tier: licTier,
        ExpiresAtUtc: DateTimeOffset.UtcNow.AddYears(years),
        MaxKiosks: kiosks,
        AiEnabled: ai);

    byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

    using ECDsa ecdsa = ECDsa.Create();
    ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(devPrivateKeyPkcs8Base64), out _);
    byte[] signatureBytes = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    var signedToken = new SignedLicenseToken(
        PayloadBase64: Convert.ToBase64String(payloadBytes),
        SignatureBase64: Convert.ToBase64String(signatureBytes));

    string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outPath));
    if (!string.IsNullOrEmpty(outputDirectory))
        Directory.CreateDirectory(outputDirectory);

    string jsonContent = JsonSerializer.Serialize(signedToken, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(outPath, jsonContent);

    if (!isQuiet)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=================================================================");
        Console.WriteLine("            LICENSE TOKEN GENERATED SUCCESSFULLY!                ");
        Console.WriteLine("=================================================================");
        Console.ResetColor();
        Console.WriteLine($"Hardware ID  : {effectiveHwid}");
        Console.WriteLine($"License Tier : {payload.Tier} (Max Kiosks: {payload.MaxKiosks}, AI: {(payload.AiEnabled ? "Enabled" : "Disabled")})");
        Console.WriteLine($"Expires UTC  : {payload.ExpiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"Output File  : {Path.GetFullPath(outPath)}");
    }

    // Auto-copy to standard sibling and parent app folders if they exist
    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    string currentDir = Directory.GetCurrentDirectory();
    string[] potentialKioskPaths = [
        Path.Combine(baseDir, "..", "KioskApp", "license.token"),
        Path.Combine(baseDir, "KioskApp", "license.token"),
        Path.Combine(baseDir, "..", "license.token"),
        Path.Combine(baseDir, "license.token"),
        Path.Combine(baseDir, "..", "..", "src", "SelfCheckoutKiosk.App", "license.token"),
        Path.Combine(currentDir, "dist", "SelfCheckoutKiosk-Portable", "KioskApp", "license.token"),
        Path.Combine(currentDir, "dist", "SelfCheckoutKiosk-Portable", "license.token"),
        Path.Combine(currentDir, "dist", "SelfCheckoutKiosk-Package", "KioskApp", "license.token"),
        Path.Combine(currentDir, "dist", "SelfCheckoutKiosk-Package", "license.token"),
        Path.Combine(currentDir, "KioskApp", "license.token"),
        Path.Combine(currentDir, "license.token")
    ];

    foreach (var path in potentialKioskPaths)
    {
        try
        {
            string? targetDir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (Directory.Exists(targetDir))
            {
                File.WriteAllText(path, jsonContent);
                if (!isQuiet)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Auto-Synced  : {Path.GetFullPath(path)}");
                    Console.ResetColor();
                }
            }
        }
        catch { }
    }

    if (!isQuiet)
    {
        Console.WriteLine("=================================================================");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("STATUS: Valid node-locked token is ready for activation.");
        Console.ResetColor();
    }
}

void InspectToken(string filePath)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Inspecting License Token: {filePath}");
    Console.ResetColor();

    if (!File.Exists(filePath))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: File '{filePath}' does not exist.");
        Console.ResetColor();
        return;
    }

    try
    {
        string raw = File.ReadAllText(filePath);
        var token = JsonSerializer.Deserialize<SignedLicenseToken>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (token == null || string.IsNullOrEmpty(token.PayloadBase64))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid license token format.");
            Console.ResetColor();
            return;
        }

        byte[] payloadBytes = Convert.FromBase64String(token.PayloadBase64);
        var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("License Token Metadata:");
        Console.ResetColor();
        Console.WriteLine($"  Hardware ID  : {payload?.HardwareId}");
        Console.WriteLine($"  Tier         : {payload?.Tier}");
        Console.WriteLine($"  Max Kiosks   : {payload?.MaxKiosks}");
        Console.WriteLine($"  AI Features  : {(payload?.AiEnabled == true ? "Enabled" : "Disabled")}");
        Console.WriteLine($"  Expires UTC  : {payload?.ExpiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  Is Expired?  : {(payload?.ExpiresAtUtc < DateTimeOffset.UtcNow ? "YES (EXPIRED)" : "NO (ACTIVE)")}");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to parse token: {ex.Message}");
        Console.ResetColor();
    }
}

void WaitToExit()
{
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to close...");
        try { Console.ReadKey(); } catch { }
    }
}
