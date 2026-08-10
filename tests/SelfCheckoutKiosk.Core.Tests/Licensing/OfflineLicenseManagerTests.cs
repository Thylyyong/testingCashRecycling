using System;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests.Licensing;

public sealed class OfflineLicenseManagerTests
{
    private const string LocalHardwareId =
        "TEST-HARDWARE-001";

    private static readonly DateTimeOffset CurrentUtc =
        new(
            2030,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero
        );

    // =========================================================
    // VALID LICENSE
    // =========================================================

    [Fact]
    public async Task
        LoadAndValidateAsync_ValidProLicense_Succeeds()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Pro
            );

        var manager =
            CreateManager(
                claims
            );

        await manager
            .LoadAndValidateAsync();

        manager.EnforceFeatureAccess(
            LicensedFeature.CashRecycler
        );
    }

    // =========================================================
    // LICENSE SOURCE
    // =========================================================

    [Fact]
    public async Task
        LoadAndValidateAsync_MissingLicense_Throws()
    {
        var source =
            new FakeLicenseConfigurationSource(
                null
            );

        var manager =
            CreateManager(
                CreateValidClaims(),
                licenseSource:
                    source
            );

        var exception =
            await Assert.ThrowsAsync<
                LicenseValidationException
            >(
                () =>
                    manager
                        .LoadAndValidateAsync()
            );

        Assert.Contains(
            "No offline license",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_EmptySignedToken_Throws()
    {
        var persistedLicense =
            CreatePersistedLicense();

        persistedLicense.SignedToken =
            " ";

        var manager =
            CreateManager(
                CreateValidClaims(),
                persistedLicense:
                    persistedLicense
            );

        var exception =
            await Assert.ThrowsAsync<
                LicenseValidationException
            >(
                () =>
                    manager
                        .LoadAndValidateAsync()
            );

        Assert.Contains(
            "signed token",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    // =========================================================
    // SIGNATURE
    // =========================================================

    [Fact]
    public async Task
        LoadAndValidateAsync_InvalidSignature_Throws()
    {
        var manager =
            CreateManager(
                claims: null
            );

        var exception =
            await Assert.ThrowsAsync<
                LicenseValidationException
            >(
                () =>
                    manager
                        .LoadAndValidateAsync()
            );

        Assert.Contains(
            "signature",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_SignatureVerifierThrows_WrapsFailure()
    {
        var manager =
            CreateManager(
                CreateValidClaims(),
                signatureException:
                    new InvalidOperationException(
                        "Verifier failure."
                    )
            );

        var exception =
            await Assert.ThrowsAsync<
                LicenseValidationException
            >(
                () =>
                    manager
                        .LoadAndValidateAsync()
            );

        Assert.NotNull(
            exception.InnerException
        );

        Assert.IsType<
            InvalidOperationException
        >(
            exception.InnerException
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_InvalidSignature_DoesNotReadHardwareId()
    {
        var hardwareProvider =
            new FakeHardwareIdProvider(
                LocalHardwareId
            );

        var manager =
            CreateManager(
                claims: null,
                hardwareIdProvider:
                    hardwareProvider
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );

        Assert.Equal(
            0,
            hardwareProvider.CallCount
        );
    }

    // =========================================================
    // EXPIRATION
    // =========================================================

    [Fact]
    public async Task
        LoadAndValidateAsync_ExpiredLicense_Throws()
    {
        var claims =
            CreateValidClaims() with
            {
                ExpiresAtUtc =
                    CurrentUtc.AddSeconds(
                        -1
                    )
            };

        var manager =
            CreateManager(
                claims
            );

        var exception =
            await Assert.ThrowsAsync<
                LicenseValidationException
            >(
                () =>
                    manager
                        .LoadAndValidateAsync()
            );

        Assert.Contains(
            "expired",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_ExpiresExactlyNow_Throws()
    {
        var claims =
            CreateValidClaims() with
            {
                ExpiresAtUtc =
                    CurrentUtc
            };

        var manager =
            CreateManager(
                claims
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_ExpiredLicense_DoesNotReadHardwareId()
    {
        var claims =
            CreateValidClaims() with
            {
                ExpiresAtUtc =
                    CurrentUtc.AddMinutes(
                        -1
                    )
            };

        var hardwareProvider =
            new FakeHardwareIdProvider(
                LocalHardwareId
            );

        var manager =
            CreateManager(
                claims,
                hardwareIdProvider:
                    hardwareProvider
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );

        Assert.Equal(
            0,
            hardwareProvider.CallCount
        );
    }

    // =========================================================
    // HARDWARE ID
    // =========================================================

    [Fact]
    public async Task
        LoadAndValidateAsync_WrongHardwareId_Throws()
    {
        var claims =
            CreateValidClaims() with
            {
                HardwareId =
                    "OTHER-HARDWARE"
            };

        var manager =
            CreateManager(
                claims
            );

        var exception =
            await Assert.ThrowsAsync<
                LicenseValidationException
            >(
                () =>
                    manager
                        .LoadAndValidateAsync()
            );

        Assert.Contains(
            "not valid for this kiosk",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_EmptyLocalHardwareId_Throws()
    {
        var manager =
            CreateManager(
                CreateValidClaims(),
                localHardwareId:
                    " "
            );

        var exception =
            await Assert.ThrowsAsync<
                LicenseValidationException
            >(
                () =>
                    manager
                        .LoadAndValidateAsync()
            );

        Assert.Contains(
            "HardwareId",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    // =========================================================
    // TRUSTED SIGNED CLAIMS
    // =========================================================

    [Fact]
    public async Task
        LoadAndValidateAsync_UsesSignedHardwareId_NotPersistedHardwareId()
    {
        var persistedLicense =
            CreatePersistedLicense();

        /*
         * The editable database record says this machine is valid.
         */
        persistedLicense.HardwareId =
            LocalHardwareId;

        /*
         * The signed token says the license belongs to another
         * machine.
         *
         * Signed claims must win.
         */
        var claims =
            CreateValidClaims() with
            {
                HardwareId =
                    "SIGNED-OTHER-MACHINE"
            };

        var manager =
            CreateManager(
                claims,
                persistedLicense:
                    persistedLicense
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );
    }

    [Fact]
    public async Task
        EnforceFeatureAccess_UsesSignedFlags_NotPersistedFlags()
    {
        var persistedLicense =
            CreatePersistedLicense();

        /*
         * Simulate local database tampering.
         */
        persistedLicense.CashModuleEnabled =
            true;

        /*
         * The cryptographically protected token says cash is
         * disabled.
         */
        var claims =
            CreateValidClaims() with
            {
                CashModuleEnabled =
                    false
            };

        var manager =
            CreateManager(
                claims,
                persistedLicense:
                    persistedLicense
            );

        await manager
            .LoadAndValidateAsync();

        Assert.Throws<
            LicenseValidationException
        >(
            () =>
                manager.EnforceFeatureAccess(
                    LicensedFeature.CashRecycler
                )
        );
    }

    // =========================================================
    // LITE TIER
    // =========================================================

    [Fact]
    public async Task
        LoadAndValidateAsync_LiteMoreThanTwoKiosks_Throws()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Lite
            ) with
            {
                MaxKiosks =
                    3
            };

        var manager =
            CreateManager(
                claims
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_LiteCashEnabled_Throws()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Lite
            ) with
            {
                CashModuleEnabled =
                    true
            };

        var manager =
            CreateManager(
                claims
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_LiteAiEnabled_Throws()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Lite
            ) with
            {
                AiModuleEnabled =
                    true
            };

        var manager =
            CreateManager(
                claims
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_LiteErpEnabled_Throws()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Lite
            ) with
            {
                ErpSyncEnabled =
                    true
            };

        var manager =
            CreateManager(
                claims
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );
    }

    // =========================================================
    // PRO TIER
    // =========================================================

    [Fact]
    public async Task
        LoadAndValidateAsync_ProMoreThanFiveKiosks_Throws()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Pro
            ) with
            {
                MaxKiosks =
                    6
            };

        var manager =
            CreateManager(
                claims
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_ProErpEnabled_Throws()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Pro
            ) with
            {
                ErpSyncEnabled =
                    true
            };

        var manager =
            CreateManager(
                claims
            );

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );
    }

    // =========================================================
    // FEATURE ACCESS
    // =========================================================

    [Fact]
    public void
        EnforceFeatureAccess_BeforeValidation_Throws()
    {
        var manager =
            CreateManager(
                CreateValidClaims()
            );

        Assert.Throws<
            LicenseValidationException
        >(
            () =>
                manager.EnforceFeatureAccess(
                    LicensedFeature.CashRecycler
                )
        );
    }

    [Fact]
    public async Task
        EnforceFeatureAccess_ProCashEnabled_Succeeds()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Pro
            ) with
            {
                CashModuleEnabled =
                    true
            };

        var manager =
            CreateManager(
                claims
            );

        await manager
            .LoadAndValidateAsync();

        manager.EnforceFeatureAccess(
            LicensedFeature.CashRecycler
        );
    }

    [Fact]
    public async Task
        EnforceFeatureAccess_ProCashDisabled_Throws()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Pro
            ) with
            {
                CashModuleEnabled =
                    false
            };

        var manager =
            CreateManager(
                claims
            );

        await manager
            .LoadAndValidateAsync();

        Assert.Throws<
            LicenseValidationException
        >(
            () =>
                manager.EnforceFeatureAccess(
                    LicensedFeature.CashRecycler
                )
        );
    }

    [Fact]
    public async Task
        EnforceFeatureAccess_ProAiEnabled_Succeeds()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Pro
            ) with
            {
                AiModuleEnabled =
                    true
            };

        var manager =
            CreateManager(
                claims
            );

        await manager
            .LoadAndValidateAsync();

        manager.EnforceFeatureAccess(
            LicensedFeature.AiCameraTelemetry
        );
    }

    [Fact]
    public async Task
        EnforceFeatureAccess_ProCustomErp_Throws()
    {
        var manager =
            CreateManager(
                CreateValidClaims(
                    LicenseTier.Pro
                )
            );

        await manager
            .LoadAndValidateAsync();

        Assert.Throws<
            LicenseValidationException
        >(
            () =>
                manager.EnforceFeatureAccess(
                    LicensedFeature.CustomErpSync
                )
        );
    }

    [Fact]
    public async Task
        EnforceFeatureAccess_EnterpriseEnabledFeatures_Succeed()
    {
        var claims =
            CreateValidClaims(
                LicenseTier.Enterprise
            ) with
            {
                MaxKiosks =
                    100,

                CashModuleEnabled =
                    true,

                AiModuleEnabled =
                    true,

                ErpSyncEnabled =
                    true
            };

        var manager =
            CreateManager(
                claims
            );

        await manager
            .LoadAndValidateAsync();

        manager.EnforceFeatureAccess(
            LicensedFeature.CashRecycler
        );

        manager.EnforceFeatureAccess(
            LicensedFeature.AiCameraTelemetry
        );

        manager.EnforceFeatureAccess(
            LicensedFeature.CustomErpSync
        );
    }

    [Fact]
    public async Task
        EnforceFeatureAccess_InvalidFeature_Throws()
    {
        var manager =
            CreateManager(
                CreateValidClaims()
            );

        await manager
            .LoadAndValidateAsync();

        Assert.Throws<
            ArgumentOutOfRangeException
        >(
            () =>
                manager.EnforceFeatureAccess(
                    (LicensedFeature)999
                )
        );
    }

    // =========================================================
    // SECURITY STATE
    // =========================================================

    [Fact]
    public async Task
        FailedReload_ClearsPreviouslyValidatedLicense()
    {
        var persistedLicense =
            CreatePersistedLicense();

        var source =
            new FakeLicenseConfigurationSource(
                persistedLicense
            );

        var verifier =
            new MutableLicenseSignatureVerifier(
                CreateValidClaims()
            );

        var manager =
            new OfflineLicenseManager(
                source,
                verifier,
                new FakeHardwareIdProvider(
                    LocalHardwareId
                ),
                new FixedTimeProvider(
                    CurrentUtc
                )
            );

        await manager
            .LoadAndValidateAsync();

        manager.EnforceFeatureAccess(
            LicensedFeature.CashRecycler
        );

        /*
         * A later validation attempt receives an expired signed
         * license.
         */
        verifier.Claims =
            CreateValidClaims() with
            {
                ExpiresAtUtc =
                    CurrentUtc.AddDays(
                        -1
                    )
            };

        await Assert.ThrowsAsync<
            LicenseValidationException
        >(
            () =>
                manager
                    .LoadAndValidateAsync()
        );

        /*
         * The previously trusted license must no longer remain
         * active after the failed reload.
         */
        Assert.Throws<
            LicenseValidationException
        >(
            () =>
                manager.EnforceFeatureAccess(
                    LicensedFeature.CashRecycler
                )
        );
    }

    [Fact]
    public async Task
        LoadAndValidateAsync_CancelledToken_Throws()
    {
        var manager =
            CreateManager(
                CreateValidClaims()
            );

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource
            .Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException
        >(
            () =>
                manager.LoadAndValidateAsync(
                    cancellationTokenSource.Token
                )
        );
    }

    // =========================================================
    // TEST FACTORY
    // =========================================================

    private static OfflineLicenseManager
        CreateManager(
            VerifiedLicenseClaims? claims,
            Exception? signatureException = null,
            string localHardwareId =
                LocalHardwareId,
            LicenseConfiguration? persistedLicense =
                null,
            ILicenseConfigurationSource? licenseSource =
                null,
            IHardwareIdProvider? hardwareIdProvider =
                null)
    {
        /*
         * A caller can provide a custom source when it needs to
         * represent special source states such as "no license".
         *
         * Otherwise a normal persisted test license is used.
         */
        var source =
            licenseSource ??
            new FakeLicenseConfigurationSource(
                persistedLicense ??
                CreatePersistedLicense()
            );

        var verifier =
            new FakeLicenseSignatureVerifier(
                claims,
                signatureException
            );

        var hardware =
            hardwareIdProvider ??
            new FakeHardwareIdProvider(
                localHardwareId
            );

        return new OfflineLicenseManager(
            source,
            verifier,
            hardware,
            new FixedTimeProvider(
                CurrentUtc
            )
        );
    }

    // =========================================================
    // TEST DATA
    // =========================================================

    private static LicenseConfiguration
        CreatePersistedLicense()
    {
        return new LicenseConfiguration
        {
            Id =
                1,

            /*
             * These persistence columns are not the runtime
             * security trust source.
             *
             * SignedToken is verified and produces the trusted
             * claims used by OfflineLicenseManager.
             */
            HardwareId =
                LocalHardwareId,

            Tier =
                LicenseTier.Pro,

            MaxKiosks =
                5,

            CashModuleEnabled =
                true,

            AiModuleEnabled =
                false,

            ErpSyncEnabled =
                false,

            ExpiresAtUtc =
                CurrentUtc.AddYears(
                    1
                ),

            SignedToken =
                "test-signed-license-token"
        };
    }

    private static VerifiedLicenseClaims
        CreateValidClaims(
            LicenseTier tier =
                LicenseTier.Pro)
    {
        return new VerifiedLicenseClaims(
            HardwareId:
                LocalHardwareId,

            Tier:
                tier,

            MaxKiosks:
                tier switch
                {
                    LicenseTier.Lite =>
                        2,

                    LicenseTier.Pro =>
                        5,

                    LicenseTier.Enterprise =>
                        100,

                    _ =>
                        1
                },

            CashModuleEnabled:
                tier !=
                LicenseTier.Lite,

            AiModuleEnabled:
                false,

            ErpSyncEnabled:
                false,

            ExpiresAtUtc:
                CurrentUtc.AddYears(
                    1
                )
        );
    }

    // =========================================================
    // FAKES
    // =========================================================

    private sealed class FakeLicenseConfigurationSource
        : ILicenseConfigurationSource
    {
        private readonly LicenseConfiguration?
            _license;

        public FakeLicenseConfigurationSource(
            LicenseConfiguration? license)
        {
            _license =
                license;
        }

        public Task<LicenseConfiguration?>
            LoadAsync(
                CancellationToken cancellationToken =
                    default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.FromResult(
                _license
            );
        }
    }

    private sealed class FakeLicenseSignatureVerifier
        : ILicenseSignatureVerifier
    {
        private readonly VerifiedLicenseClaims?
            _claims;

        private readonly Exception?
            _exception;

        public FakeLicenseSignatureVerifier(
            VerifiedLicenseClaims? claims,
            Exception? exception = null)
        {
            _claims =
                claims;

            _exception =
                exception;
        }

        public VerifiedLicenseClaims?
            VerifyAndRead(
                string signedToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                signedToken
            );

            if (
                _exception is
                not null
            )
            {
                throw _exception;
            }

            return _claims;
        }
    }

    private sealed class MutableLicenseSignatureVerifier
        : ILicenseSignatureVerifier
    {
        public MutableLicenseSignatureVerifier(
            VerifiedLicenseClaims? claims)
        {
            Claims =
                claims;
        }

        public VerifiedLicenseClaims?
            Claims
        {
            get;
            set;
        }

        public VerifiedLicenseClaims?
            VerifyAndRead(
                string signedToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                signedToken
            );

            return Claims;
        }
    }

    private sealed class FakeHardwareIdProvider
        : IHardwareIdProvider
    {
        private readonly string
            _hardwareId;

        public FakeHardwareIdProvider(
            string hardwareId)
        {
            _hardwareId =
                hardwareId;
        }

        public int CallCount
        {
            get;
            private set;
        }

        public string GetHardwareId()
        {
            CallCount++;

            return _hardwareId;
        }
    }

    private sealed class FixedTimeProvider
        : TimeProvider
    {
        private readonly DateTimeOffset
            _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow;
        }

        public override DateTimeOffset
            GetUtcNow()
        {
            return _utcNow;
        }
    }
}