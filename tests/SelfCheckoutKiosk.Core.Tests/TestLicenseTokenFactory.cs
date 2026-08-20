using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Tests;

/// <summary>
/// Builds a validly-signed license token for tests, using
/// <see cref="OfflineLicenseManager"/>'s own public surface (constructor
/// accepts an injected public key + token path) rather than touching
/// Core/Licensing internals. Mirrors <see cref="FallbackHardwareIdProvider"/>'s
/// hashing so the manager's node-lock check passes under test.
/// </summary>
internal static class TestLicenseTokenFactory
{
    /// <summary>Writes a signed token to <paramref name="tokenFilePath"/> and
    /// returns the DER-encoded SPKI public key to construct
    /// <see cref="OfflineLicenseManager"/> with.</summary>
    public static byte[] WriteValidToken(string tokenFilePath, LicenseTier tier = LicenseTier.Enterprise, bool aiEnabled = true)
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string hardwareId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName)));

        var payload = new LicensePayload(hardwareId, tier, DateTimeOffset.UtcNow.AddYears(1), MaxKiosks: 5, AiEnabled: aiEnabled);
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] signature = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        var signed = new SignedLicenseToken(Convert.ToBase64String(payloadBytes), Convert.ToBase64String(signature));
        File.WriteAllText(tokenFilePath, JsonSerializer.Serialize(signed));

        return ecdsa.ExportSubjectPublicKeyInfo();
    }
}
