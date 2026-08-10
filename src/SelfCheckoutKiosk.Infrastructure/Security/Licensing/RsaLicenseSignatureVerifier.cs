using System.Security.Cryptography;
using System.Text.Json;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Infrastructure.Security.Licensing;

/// <summary>
/// Verifies asymmetrically signed offline-license tokens.
///
/// Cryptographic profile:
///
/// - RSA
/// - SHA-256
/// - RSA-PSS padding
///
/// Token format:
///
/// Base64Url(payload-json).Base64Url(signature)
///
/// The signature protects the decoded JSON payload bytes.
///
/// Only a public verification key is accepted by this class.
/// Private signing keys must never be stored on the kiosk.
/// </summary>
public sealed class RsaLicenseSignatureVerifier
    : ILicenseSignatureVerifier
{
    /*
     * Normal license tokens should be very small.
     *
     * This limit prevents unexpectedly large input from being
     * processed by the licensing path.
     */
    private const int MaxTokenLength =
        32 * 1024;

    private readonly string
        _publicKeyPem;

    /// <summary>
    /// Creates the verifier using an RSA public verification key.
    /// </summary>
    public RsaLicenseSignatureVerifier(
        string publicKeyPem)
    {
        if (
            string.IsNullOrWhiteSpace(
                publicKeyPem
            )
        )
        {
            throw new ArgumentException(
                "The license public key must not be empty.",
                nameof(publicKeyPem)
            );
        }

        ValidatePublicKeyPem(
            publicKeyPem
        );

        _publicKeyPem =
            publicKeyPem;
    }

    /// <summary>
    /// Verifies the signed token and returns the claims protected
    /// by its asymmetric signature.
    ///
    /// Returns null for malformed, unsupported, or cryptographically
    /// invalid tokens.
    /// </summary>
    public VerifiedLicenseClaims?
        VerifyAndRead(
            string signedToken)
    {
        if (
            string.IsNullOrWhiteSpace(
                signedToken
            )
        )
        {
            return null;
        }

        if (
            signedToken.Length >
            MaxTokenLength
        )
        {
            return null;
        }

        /*
         * =====================================================
         * TOKEN STRUCTURE
         * =====================================================
         *
         * Required:
         *
         * payload.signature
         */
        if (
            !TrySplitToken(
                signedToken,
                out var payloadSegment,
                out var signatureSegment
            )
        )
        {
            return null;
        }

        /*
         * =====================================================
         * BASE64URL
         * =====================================================
         */

        if (
            !TryDecodeBase64Url(
                payloadSegment,
                out var payloadBytes
            )
        )
        {
            return null;
        }

        if (
            !TryDecodeBase64Url(
                signatureSegment,
                out var signatureBytes
            )
        )
        {
            return null;
        }

        if (
            payloadBytes.Length ==
            0 ||
            signatureBytes.Length ==
            0
        )
        {
            return null;
        }

        /*
         * =====================================================
         * SIGNATURE AUTHENTICITY
         * =====================================================
         *
         * This happens BEFORE the JSON claims are deserialized.
         *
         * No payload value becomes trusted before the signature
         * has been authenticated.
         */
        if (
            !VerifySignature(
                payloadBytes,
                signatureBytes
            )
        )
        {
            return null;
        }

        /*
         * =====================================================
         * VERIFIED PAYLOAD PARSING
         * =====================================================
         *
         * We only deserialize after successful cryptographic
         * verification.
         */
        LicenseTokenPayload?
            payload;

        try
        {
            payload =
                JsonSerializer.Deserialize(
                    payloadBytes,
                    LicenseTokenJsonContext
                        .Default
                        .LicenseTokenPayload
                );
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        if (
            payload is null
        )
        {
            return null;
        }

        /*
         * The transport payload has now been authenticated.
         *
         * Convert it into the Core representation used by
         * OfflineLicenseManager.
         */
        return ConvertToVerifiedClaims(
            payload
        );
    }

    // =========================================================
    // SIGNATURE
    // =========================================================

    /// <summary>
    /// Verifies RSA-PSS/SHA-256 over the exact decoded payload
    /// bytes.
    /// </summary>
    private bool VerifySignature(
        byte[] payloadBytes,
        byte[] signatureBytes)
    {
        try
        {
            using var rsa =
                RSA.Create();

            rsa.ImportFromPem(
                _publicKeyPem
            );

            return rsa.VerifyData(
                payloadBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss
            );
        }
        catch (CryptographicException)
        {
            /*
             * Malformed signature or unsupported cryptographic
             * operation must fail closed.
             */
            return false;
        }
    }

    // =========================================================
    // CLAIM CONVERSION
    // =========================================================

    /// <summary>
    /// Converts the authenticated transport payload into Core's
    /// trusted license-claim representation.
    ///
    /// Business rules such as expiration, HardwareId matching,
    /// kiosk-count limits, and feature-tier consistency remain
    /// the responsibility of OfflineLicenseManager.
    /// </summary>
    private static VerifiedLicenseClaims?
        ConvertToVerifiedClaims(
            LicenseTokenPayload payload)
    {
        if (
            string.IsNullOrWhiteSpace(
                payload.HardwareId
            )
        )
        {
            return null;
        }

        if (
            string.IsNullOrWhiteSpace(
                payload.Tier
            )
        )
        {
            return null;
        }

        LicenseTier?
            tier =
                payload.Tier switch
                {
                    nameof(
                        LicenseTier.Lite
                    ) =>
                        LicenseTier.Lite,

                    nameof(
                        LicenseTier.Pro
                    ) =>
                        LicenseTier.Pro,

                    nameof(
                        LicenseTier.Enterprise
                    ) =>
                        LicenseTier.Enterprise,

                    _ =>
                        null
                };

        if (
            tier is null
        )
        {
            return null;
        }

        if (
            payload.ExpiresAtUtc ==
            default
        )
        {
            return null;
        }

        return new VerifiedLicenseClaims(
            HardwareId:
                payload
                    .HardwareId
                    .Trim(),

            Tier:
                tier.Value,

            MaxKiosks:
                payload.MaxKiosks,

            CashModuleEnabled:
                payload.CashModuleEnabled,

            AiModuleEnabled:
                payload.AiModuleEnabled,

            ErpSyncEnabled:
                payload.ErpSyncEnabled,

            ExpiresAtUtc:
                payload
                    .ExpiresAtUtc
                    .ToUniversalTime()
        );
    }

    // =========================================================
    // TOKEN FORMAT
    // =========================================================

    /// <summary>
    /// Splits:
    ///
    /// payload.signature
    ///
    /// Exactly one separator is permitted.
    /// </summary>
    private static bool TrySplitToken(
        string token,
        out string payloadSegment,
        out string signatureSegment)
    {
        payloadSegment =
            string.Empty;

        signatureSegment =
            string.Empty;

        var separatorIndex =
            token.IndexOf(
                '.'
            );

        if (
            separatorIndex <=
            0
        )
        {
            return false;
        }

        if (
            separatorIndex >=
            token.Length - 1
        )
        {
            return false;
        }

        /*
         * There must be exactly one '.'.
         */
        if (
            token.IndexOf(
                '.',
                separatorIndex + 1
            ) >=
            0
        )
        {
            return false;
        }

        payloadSegment =
            token[
                ..separatorIndex
            ];

        signatureSegment =
            token[
                (separatorIndex + 1)..
            ];

        return
            payloadSegment.Length >
                0 &&
            signatureSegment.Length >
                0;
    }

    // =========================================================
    // BASE64URL
    // =========================================================

    /// <summary>
    /// Decodes strict, unpadded Base64Url.
    ///
    /// Accepted alphabet:
    ///
    /// A-Z
    /// a-z
    /// 0-9
    /// -
    /// _
    ///
    /// Standard Base64 characters +, /, = and whitespace are
    /// deliberately rejected.
    /// </summary>
    private static bool TryDecodeBase64Url(
        string value,
        out byte[] bytes)
    {
        bytes =
            [];

        if (
            !IsStrictBase64Url(
                value
            )
        )
        {
            return false;
        }

        /*
         * Base64Url:
         *
         * - becomes +
         * _ becomes /
         *
         * Padding is restored only for Convert.FromBase64String.
         */
        var base64 =
            value
                .Replace(
                    '-',
                    '+'
                )
                .Replace(
                    '_',
                    '/'
                );

        var remainder =
            base64.Length %
            4;

        switch (remainder)
        {
            case 0:
                break;

            case 2:
                base64 +=
                    "==";

                break;

            case 3:
                base64 +=
                    "=";

                break;

            default:
                /*
                 * A Base64 string can never have a valid encoded
                 * length with remainder 1.
                 */
                return false;
        }

        try
        {
            bytes =
                Convert.FromBase64String(
                    base64
                );

            return true;
        }
        catch (FormatException)
        {
            bytes =
                [];

            return false;
        }
    }

    /// <summary>
    /// Ensures that the token uses only the canonical unpadded
    /// Base64Url alphabet.
    /// </summary>
    private static bool IsStrictBase64Url(
        string value)
    {
        if (
            string.IsNullOrEmpty(
                value
            )
        )
        {
            return false;
        }

        foreach (
            var character
            in value
        )
        {
            var valid =
                character is >= 'A' and <= 'Z' ||
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character ==
                    '-' ||
                character ==
                    '_';

            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    // =========================================================
    // PUBLIC KEY
    // =========================================================

    /// <summary>
    /// Ensures that:
    ///
    /// - the configured PEM is an RSA public key;
    /// - private signing material is not present;
    /// - the key can be imported by the runtime.
    /// </summary>
    private static void ValidatePublicKeyPem(
        string publicKeyPem)
    {
        /*
         * The kiosk must never contain the private signing key.
         */
        if (
            publicKeyPem.Contains(
                "PRIVATE KEY",
                StringComparison.Ordinal
            )
        )
        {
            throw new ArgumentException(
                "A private signing key must never be configured " +
                "on the kiosk.",
                nameof(publicKeyPem)
            );
        }

        /*
         * RSA.ImportFromPem supports both SubjectPublicKeyInfo
         * PUBLIC KEY and PKCS#1 RSA PUBLIC KEY PEM labels.
         */
        var containsSupportedPublicKey =
            publicKeyPem.Contains(
                "-----BEGIN PUBLIC KEY-----",
                StringComparison.Ordinal
            ) ||
            publicKeyPem.Contains(
                "-----BEGIN RSA PUBLIC KEY-----",
                StringComparison.Ordinal
            );

        if (!containsSupportedPublicKey)
        {
            throw new ArgumentException(
                "The configured license key is not a supported " +
                "RSA public-key PEM.",
                nameof(publicKeyPem)
            );
        }

        try
        {
            using var rsa =
                RSA.Create();

            rsa.ImportFromPem(
                publicKeyPem
            );

            /*
             * Confirm usable RSA public parameters were loaded.
             */
            var parameters =
                rsa.ExportParameters(
                    includePrivateParameters:
                        false
                );

            if (
                parameters.Modulus is
                    null ||
                parameters.Modulus.Length ==
                    0 ||
                parameters.Exponent is
                    null ||
                parameters.Exponent.Length ==
                    0
            )
            {
                throw new ArgumentException(
                    "The configured license public key does not " +
                    "contain usable RSA public parameters.",
                    nameof(publicKeyPem)
                );
            }
        }
        catch (
            CryptographicException exception
        )
        {
            throw new ArgumentException(
                "The configured license public key is invalid.",
                nameof(publicKeyPem),
                exception
            );
        }
        catch (
            ArgumentException exception
        )
        {
            throw new ArgumentException(
                "The configured license public key is invalid.",
                nameof(publicKeyPem),
                exception
            );
        }
    }
}