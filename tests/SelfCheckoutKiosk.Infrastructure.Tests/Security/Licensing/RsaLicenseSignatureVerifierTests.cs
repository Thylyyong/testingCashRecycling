using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Infrastructure.Security.Licensing;
using Xunit;

namespace SelfCheckoutKiosk.Infrastructure.Tests.Security.Licensing;

public sealed class RsaLicenseSignatureVerifierTests
{
    private const string HardwareId =
        "KIOSK-TEST-HARDWARE-001";

    private static readonly DateTimeOffset
        ExpiresAtUtc =
            new(
                2035,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero
            );

    // =========================================================
    // VALID TOKEN
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_ValidToken_ReturnsClaims()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var token =
            CreateSignedToken(
                rsa,
                tier:
                    "Pro",
                maxKiosks:
                    5,
                cashEnabled:
                    true,
                aiEnabled:
                    true,
                erpEnabled:
                    false
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        Assert.NotNull(
            result
        );

        Assert.Equal(
            HardwareId,
            result.HardwareId
        );

        Assert.Equal(
            LicenseTier.Pro,
            result.Tier
        );

        Assert.Equal(
            5,
            result.MaxKiosks
        );

        Assert.True(
            result.CashModuleEnabled
        );

        Assert.True(
            result.AiModuleEnabled
        );

        Assert.False(
            result.ErpSyncEnabled
        );

        Assert.Equal(
            ExpiresAtUtc,
            result.ExpiresAtUtc
        );
    }

    // =========================================================
    // INVALID SIGNATURE
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_TokenSignedByDifferentKey_ReturnsNull()
    {
        using var trustedRsa =
            RSA.Create(
                2048
            );

        using var attackerRsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                trustedRsa
            );

        var token =
            CreateSignedToken(
                attackerRsa
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        Assert.Null(
            result
        );
    }

    // =========================================================
    // TAMPERED PAYLOAD
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_TamperedPayload_ReturnsNull()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var originalToken =
            CreateSignedToken(
                rsa,
                cashEnabled:
                    false
            );

        var separator =
            originalToken
                .IndexOf(
                    '.'
                );

        var signatureSegment =
            originalToken[
                (separator + 1)..
            ];

        /*
         * Create a different payload while preserving the
         * original signature.
         */
        var tamperedPayload =
            CreatePayloadBytes(
                tier:
                    "Pro",
                maxKiosks:
                    5,
                cashEnabled:
                    true,
                aiEnabled:
                    false,
                erpEnabled:
                    false
            );

        var tamperedToken =
            Base64Url.EncodeToString(
                tamperedPayload
            ) +
            "." +
            signatureSegment;

        var result =
            verifier
                .VerifyAndRead(
                    tamperedToken
                );

        Assert.Null(
            result
        );
    }

    // =========================================================
    // MALFORMED TOKEN
    // =========================================================

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData(".abc")]
    [InlineData("abc.")]
    [InlineData("abc.def.ghi")]
    [InlineData("abc+.def")]
    [InlineData("abc.def=")]
    public void
        VerifyAndRead_MalformedToken_ReturnsNull(
            string token)
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        Assert.Null(
            result
        );
    }

    // =========================================================
    // UNSUPPORTED TIER
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_UnsupportedSignedTier_ReturnsNull()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var token =
            CreateSignedToken(
                rsa,
                tier:
                    "Ultimate"
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        Assert.Null(
            result
        );
    }

    // =========================================================
    // CASE-SENSITIVE TIER
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_LowercaseTier_ReturnsNull()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var token =
            CreateSignedToken(
                rsa,
                tier:
                    "pro"
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        Assert.Null(
            result
        );
    }

    // =========================================================
    // HARDWARE ID
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_MissingHardwareId_ReturnsNull()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var payload =
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    hardwareId =
                        "",

                    tier =
                        "Pro",

                    maxKiosks =
                        5,

                    cashModuleEnabled =
                        true,

                    aiModuleEnabled =
                        false,

                    erpSyncEnabled =
                        false,

                    expiresAtUtc =
                        ExpiresAtUtc
                }
            );

        var token =
            SignPayload(
                rsa,
                payload
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        Assert.Null(
            result
        );
    }

    // =========================================================
    // EXPIRATION SEPARATION OF RESPONSIBILITIES
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_ExpiredButAuthenticToken_ReturnsClaims()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var expiredAt =
            new DateTimeOffset(
                2020,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero
            );

        var token =
            CreateSignedToken(
                rsa,
                expiresAtUtc:
                    expiredAt
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        /*
         * Signature verifier authenticates and parses.
         *
         * OfflineLicenseManager owns expiration policy.
         */
        Assert.NotNull(
            result
        );

        Assert.Equal(
            expiredAt,
            result.ExpiresAtUtc
        );
    }

    // =========================================================
    // PRIVATE KEY MUST NEVER BE ACCEPTED
    // =========================================================

    [Fact]
    public void
        Constructor_PrivateKeyPem_Throws()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var privateKey =
            rsa.ExportPkcs8PrivateKeyPem();

        var exception =
            Assert.Throws<
                ArgumentException
            >(
                () =>
                    new RsaLicenseSignatureVerifier(
                        privateKey
                    )
            );

        Assert.Contains(
            "private",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    // =========================================================
    // INVALID PUBLIC KEY
    // =========================================================

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-public-key")]
    public void
        Constructor_InvalidPublicKey_Throws(
            string publicKey)
    {
        Assert.Throws<
            ArgumentException
        >(
            () =>
                new RsaLicenseSignatureVerifier(
                    publicKey
                )
        );
    }

    // =========================================================
    // LITE CLAIMS
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_ValidLiteToken_ReturnsLiteClaims()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var token =
            CreateSignedToken(
                rsa,
                tier:
                    "Lite",
                maxKiosks:
                    2,
                cashEnabled:
                    false,
                aiEnabled:
                    false,
                erpEnabled:
                    false
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        Assert.NotNull(
            result
        );

        Assert.Equal(
            LicenseTier.Lite,
            result.Tier
        );

        Assert.Equal(
            2,
            result.MaxKiosks
        );
    }

    // =========================================================
    // ENTERPRISE CLAIMS
    // =========================================================

    [Fact]
    public void
        VerifyAndRead_ValidEnterpriseToken_ReturnsEnterpriseClaims()
    {
        using var rsa =
            RSA.Create(
                2048
            );

        var verifier =
            CreateVerifier(
                rsa
            );

        var token =
            CreateSignedToken(
                rsa,
                tier:
                    "Enterprise",
                maxKiosks:
                    100,
                cashEnabled:
                    true,
                aiEnabled:
                    true,
                erpEnabled:
                    true
            );

        var result =
            verifier
                .VerifyAndRead(
                    token
                );

        Assert.NotNull(
            result
        );

        Assert.Equal(
            LicenseTier.Enterprise,
            result.Tier
        );

        Assert.True(
            result.ErpSyncEnabled
        );
    }

    // =========================================================
    // FACTORIES
    // =========================================================

    private static RsaLicenseSignatureVerifier
        CreateVerifier(
            RSA rsa)
    {
        var publicKey =
            rsa.ExportSubjectPublicKeyInfoPem();

        return new RsaLicenseSignatureVerifier(
            publicKey
        );
    }

    private static string
        CreateSignedToken(
            RSA rsa,
            string tier =
                "Pro",
            int maxKiosks =
                5,
            bool cashEnabled =
                true,
            bool aiEnabled =
                false,
            bool erpEnabled =
                false,
            DateTimeOffset? expiresAtUtc =
                null)
    {
        var payload =
            CreatePayloadBytes(
                tier,
                maxKiosks,
                cashEnabled,
                aiEnabled,
                erpEnabled,
                expiresAtUtc
            );

        return SignPayload(
            rsa,
            payload
        );
    }

    private static byte[]
        CreatePayloadBytes(
            string tier,
            int maxKiosks,
            bool cashEnabled,
            bool aiEnabled,
            bool erpEnabled,
            DateTimeOffset? expiresAtUtc =
                null)
    {
        return JsonSerializer
            .SerializeToUtf8Bytes(
                new
                {
                    hardwareId =
                        HardwareId,

                    tier =
                        tier,

                    maxKiosks =
                        maxKiosks,

                    cashModuleEnabled =
                        cashEnabled,

                    aiModuleEnabled =
                        aiEnabled,

                    erpSyncEnabled =
                        erpEnabled,

                    expiresAtUtc =
                        expiresAtUtc ??
                        ExpiresAtUtc
                }
            );
    }

    private static string
        SignPayload(
            RSA rsa,
            byte[] payloadBytes)
    {
        var signature =
            rsa.SignData(
                payloadBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss
            );

        var payloadSegment =
            Base64Url.EncodeToString(
                payloadBytes
            );

        var signatureSegment =
            Base64Url.EncodeToString(
                signature
            );

        return
            payloadSegment +
            "." +
            signatureSegment;
    }
}