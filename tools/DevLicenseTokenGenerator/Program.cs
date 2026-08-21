using System.Security.Cryptography;
using System.Text.Json;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Infrastructure.Security;

// DEV-ONLY tool. Signs a license.token for THIS machine's node-locked
// hardware id, using a throwaway ECDSA P-256 key that carries zero
// production trust — see DevLicenseKeys.cs in SelfCheckoutKiosk.App for the
// matching public half OfflineLicenseManager verifies against. Never point
// this at a real device that will ever carry a production license.

string outputPath = "license.token";
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--output" or "-o")
        outputPath = args[i + 1];
}

// Same node-lock source OfflineLicenseManager's default constructor uses —
// keep this in sync with SelfCheckoutKiosk.Infrastructure.Security.HardwareIdProvider
// so the generated token actually validates against THIS machine.
string hardwareId = new HardwareIdProvider().GetHardwareId();

var payload = new LicensePayload(
    HardwareId: hardwareId,
    Tier: LicenseTier.Enterprise, // unlimited kiosks, AI features unlocked — no dev-loop friction
    ExpiresAtUtc: DateTimeOffset.UtcNow.AddYears(5),
    MaxKiosks: 100,
    AiEnabled: true);

byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

// Matching PRIVATE key for SelfCheckoutKiosk.App.Composition.DevLicenseKeys.PublicKeyOrNull.
// Regenerate both together (a fresh ECDsa.Create(ECCurve.NamedCurves.nistP256),
// re-export SPKI + PKCS8) whenever you want a clean rotation — there is no
// production trust riding on this pair, so there's no ceremony required.
const string devPrivateKeyPkcs8Base64 =
    "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgLwr4Bk3wyGHDa6rTUxHHmNPGd35S5w5IvFvSkanylB+hRANCAASryF6RMBz+hO0eAo1luGoQxjOw7Dz0NxWrBcQADIcK4nSyEMHyJKKsxfqlfc9tcRuJfk6XXIHmdOeIvM4CoRKu";

using ECDsa ecdsa = ECDsa.Create();
ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(devPrivateKeyPkcs8Base64), out _);

// Rfc3279DerSequence — same signature format OfflineLicenseManager.LoadAndValidateAsync
// verifies against. A plain/IEEE P1363 signature would fail verification silently.
byte[] signatureBytes = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

var signedToken = new SignedLicenseToken(
    PayloadBase64: Convert.ToBase64String(payloadBytes),
    SignatureBase64: Convert.ToBase64String(signatureBytes));

string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
if (!string.IsNullOrEmpty(outputDirectory))
    Directory.CreateDirectory(outputDirectory);

File.WriteAllText(outputPath, JsonSerializer.Serialize(signedToken));

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("=================================================================");
Console.WriteLine("        SELF-CHECKOUT KIOSK - OFFLINE LICENSE GENERATOR          ");
Console.WriteLine("=================================================================");
Console.ResetColor();
Console.WriteLine($"Hardware ID  : {hardwareId}");
Console.WriteLine($"License Tier : {payload.Tier} (Max Kiosks: {payload.MaxKiosks}, AI: {(payload.AiEnabled ? "Enabled" : "Disabled")})");
Console.WriteLine($"Expires UTC  : {payload.ExpiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
Console.WriteLine($"Primary File : {Path.GetFullPath(outputPath)}");

// Also automatically copy to neighboring KioskApp folder if present in deployment package
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string[] potentialKioskPaths = [
    Path.Combine(baseDir, "..", "KioskApp", "license.token"),
    Path.Combine(baseDir, "KioskApp", "license.token"),
    Path.Combine(baseDir, "..", "..", "src", "SelfCheckoutKiosk.App", "license.token")
];

foreach (var path in potentialKioskPaths)
{
    try
    {
        string? targetDir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (Directory.Exists(targetDir))
        {
            File.WriteAllText(path, JsonSerializer.Serialize(signedToken));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Auto-Copied  : {Path.GetFullPath(path)}");
            Console.ResetColor();
        }
    }
    catch { }
}

Console.WriteLine("=================================================================");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("SUCCESS: License token is ready for use on this machine!");
Console.ResetColor();

if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to close this window...");
    try { Console.ReadKey(); } catch { }
}
