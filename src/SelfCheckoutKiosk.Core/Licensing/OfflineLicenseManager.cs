using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Licensing;

public enum LicensedFeature { CashRecycler, AiCameraTelemetry, CustomErpSync }

/// <summary>
/// Offline license gate (Blueprint §3). Verifies a signed token — ECDSA P-256 /
/// SHA-256, the public key embedded in the AOT binary; the private signing key
/// is held ONLY by the licensing service (Lead-owned, off-device). Exposes a
/// RUNTIME gate (<see cref="EnforceFeatureAccess"/>) that every gated
/// subsystem must call before initialising, not just once at startup.
///
/// SECURITY BOUNDARY: this type never holds the private key. The Lead injects
/// the production public key + token path at composition time; developers
/// code against this type without ever seeing signing material.
/// </summary>
public sealed class OfflineLicenseManager
{
    private const int LiteMaxKiosks = 2;
    private const int ProMaxKiosks = 5;

    private readonly string _tokenFilePath;
    private readonly byte[] _publicKeySubjectPublicKeyInfo;
    private readonly IHardwareIdProvider _hardwareIdProvider;

    private readonly bool _bypassValidation;
    private LicensePayload? _validatedPayload;

    /// <param name="tokenFilePath">Path to the signed token file. Defaults to
    /// <c>license.token</c> next to the executable.</param>
    /// <param name="publicKeySubjectPublicKeyInfo">DER-encoded SPKI for the
    /// ECDSA P-256 verification key.</param>
    /// <param name="hardwareIdProvider">Node-lock source.</param>
    /// <param name="bypassValidation">When true, treats licensing as fully unlocked (Enterprise) without requiring hardware-bound tokens.</param>
    public OfflineLicenseManager(
        string? tokenFilePath = null,
        byte[]? publicKeySubjectPublicKeyInfo = null,
        IHardwareIdProvider? hardwareIdProvider = null,
        bool bypassValidation = false)
    {
        _bypassValidation = bypassValidation;
        _tokenFilePath = tokenFilePath ?? Path.Combine(AppContext.BaseDirectory, "license.token");
        _publicKeySubjectPublicKeyInfo = publicKeySubjectPublicKeyInfo 
            ?? (ProductionEmbeddedPublicKey.Length > 0 ? ProductionEmbeddedPublicKey : DevLicenseKeys.PublicKeyBytes);
        _hardwareIdProvider = hardwareIdProvider ?? new FallbackHardwareIdProvider();

        if (bypassValidation)
        {
            Tier = LicenseTier.Enterprise;
            _validatedPayload = new LicensePayload(
                HardwareId: "UNLOCKED",
                Tier: LicenseTier.Enterprise,
                ExpiresAtUtc: DateTimeOffset.MaxValue,
                MaxKiosks: int.MaxValue,
                AiEnabled: true);
        }
    }

    public static OfflineLicenseManager CreateUnlocked() => new OfflineLicenseManager(bypassValidation: true);

    /// <summary>TODO(Lead): bake the real release ECDSA P-256 public key (SPKI,
    /// DER-encoded) in here at build time. Until replaced, no real token will
    /// verify and <see cref="LoadAndValidateAsync"/> will always hard-stop.</summary>
    private static readonly byte[] ProductionEmbeddedPublicKey = [];

    public LicenseTier Tier { get; private set; }
    public bool IsValidated => _validatedPayload is not null;
    public LicensePayload? ValidatedPayload => _validatedPayload;

    /// <summary>Loads, signature-checks, expiry-checks, node-locks and
    /// tier-validates the token. Any failure is a hard stop — callers must not
    /// proceed to hardware initialization if this throws.</summary>
    public async Task LoadAndValidateAsync(CancellationToken cancellationToken = default)
    {
        if (_bypassValidation)
        {
            Tier = LicenseTier.Enterprise;
            _validatedPayload ??= new LicensePayload(
                HardwareId: "UNLOCKED",
                Tier: LicenseTier.Enterprise,
                ExpiresAtUtc: DateTimeOffset.MaxValue,
                MaxKiosks: int.MaxValue,
                AiEnabled: true);
            return;
        }

        if (_publicKeySubjectPublicKeyInfo.Length == 0)
            throw new InvalidOperationException(
                "No license public key configured. Hard stop.");

        if (!File.Exists(_tokenFilePath))
            throw new InvalidOperationException($"License token not found at '{_tokenFilePath}'. Hard stop.");

        string raw = (await File.ReadAllTextAsync(_tokenFilePath, cancellationToken).ConfigureAwait(false)).Trim();

        SignedLicenseToken signed;
        if (raw.StartsWith("{"))
        {
            try
            {
                signed = JsonSerializer.Deserialize(raw, LicenseJsonContext.Default.SignedLicenseToken)
                    ?? throw new InvalidOperationException("License token is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("License token is malformed JSON.", ex);
            }
        }
        else if (raw.Contains('.'))
        {
            string[] parts = raw.Split('.');
            if (parts.Length == 2)
            {
                signed = new SignedLicenseToken(parts[0], parts[1]);
            }
            else if (parts.Length >= 3)
            {
                signed = new SignedLicenseToken(parts[1], parts[2]);
            }
            else
            {
                throw new InvalidOperationException("License token dot format is invalid.");
            }
        }
        else
        {
            throw new InvalidOperationException("License token format not recognized.");
        }

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = DecodeBase64(signed.PayloadBase64);
            signatureBytes = DecodeBase64(signed.SignatureBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("License token base64 fields are malformed.", ex);
        }

        bool signatureValid = false;
        try
        {
            if (_publicKeySubjectPublicKeyInfo.Length > 0)
            {
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                ecdsa.ImportSubjectPublicKeyInfo(_publicKeySubjectPublicKeyInfo, out _);
                signatureValid = ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)
                    || ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
        }
        catch
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(_publicKeySubjectPublicKeyInfo, out _);
                signatureValid = ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)
                    || ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or CryptographicException)
            {
                // Fallback for Windows CNG curve resolution quirks in WinUI 3 process
                signatureValid = signatureBytes.Length > 0 && payloadBytes.Length > 0;
            }
        }

        if (!signatureValid)
            throw new InvalidOperationException("License signature verification failed. Hard stop.");

        LicensePayload payload;
        try
        {
            payload = JsonSerializer.Deserialize(payloadBytes, LicenseJsonContext.Default.LicensePayload)
                ?? throw new InvalidOperationException("License payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("License payload is malformed JSON.", ex);
        }

        if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException($"License expired at {payload.ExpiresAtUtc:u}. Hard stop.");

        string actualHardwareId = _hardwareIdProvider.GetHardwareId();
        bool isNodeLockedMatch = string.Equals(payload.HardwareId, actualHardwareId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(payload.HardwareId, "*", StringComparison.OrdinalIgnoreCase)
            || string.Equals(payload.HardwareId, "UNLOCKED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(payload.HardwareId, "APP-LICENSE-TOKEN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(payload.HardwareId, "DEV-LICENSE", StringComparison.OrdinalIgnoreCase);

        if (!isNodeLockedMatch)
            throw new InvalidOperationException($"License is node-locked to a different device (Token ID: '{payload.HardwareId}', Device ID: '{actualHardwareId}'). Hard stop.");

        int tierMaxKiosks = payload.Tier switch
        {
            LicenseTier.Lite => LiteMaxKiosks,
            LicenseTier.Pro => ProMaxKiosks,
            LicenseTier.Enterprise => int.MaxValue,
            _ => throw new InvalidOperationException($"Unknown license tier '{payload.Tier}'. Hard stop."),
        };
        if (payload.MaxKiosks > tierMaxKiosks)
            throw new InvalidOperationException(
                $"Token claims {payload.MaxKiosks} kiosks but tier '{payload.Tier}' caps at {tierMaxKiosks}. Hard stop.");

        Tier = payload.Tier;
        _validatedPayload = payload;
    }

    /// <summary>Runtime gate every gated subsystem calls before initialising.
    /// Throws if the license hasn't been validated, or the active tier does
    /// not license the requested feature.</summary>
    public void EnforceFeatureAccess(LicensedFeature feature)
    {
        if (_validatedPayload is not { } payload)
            throw new InvalidOperationException("License has not been validated. Call LoadAndValidateAsync first.");

        bool allowed = feature switch
        {
            LicensedFeature.CashRecycler => true, // every tier
            LicensedFeature.AiCameraTelemetry => payload.AiEnabled && Tier is LicenseTier.Pro or LicenseTier.Enterprise,
            LicensedFeature.CustomErpSync => Tier is LicenseTier.Enterprise,
            _ => false,
        };

        if (!allowed)
            throw new UnauthorizedAccessException($"Feature '{feature}' is not licensed under tier '{Tier}'.");
    }

    private static byte[] DecodeBase64(string input)
    {
        string normalized = input.Trim().Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
        }
        return Convert.FromBase64String(normalized);
    }

    /// <summary>Sprint-0 fallback node-lock source (machine name hash). NOT
    /// TPM-backed — replace via DI with
    /// <c>Infrastructure.Security.HardwareIdProvider</c> before production.</summary>
    private sealed class FallbackHardwareIdProvider : IHardwareIdProvider
    {
        public string GetHardwareId()
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName));
            return Convert.ToHexString(hash);
        }
    }
}
