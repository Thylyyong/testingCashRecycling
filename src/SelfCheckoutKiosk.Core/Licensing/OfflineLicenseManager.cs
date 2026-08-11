using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Licensing;

/// <summary>
/// Runtime features controlled by the kiosk's offline license.
/// </summary>
public enum LicensedFeature
{
    CashRecycler = 0,
    AiCameraTelemetry = 1,
    CustomErpSync = 2
}

/// <summary>
/// Loads, validates, and enforces the kiosk's signed offline
/// license.
///
/// Validation order:
///
/// 1. Load the locally persisted signed token.
/// 2. Verify the asymmetric signature and obtain trusted claims.
/// 3. Validate expiration.
/// 4. Validate the HardwareId node lock.
/// 5. Validate the license-tier bracket.
///
/// Only claims that pass every validation stage become active.
/// </summary>
public sealed class OfflineLicenseManager
    : IOfflineLicenseManager
{
    private readonly ILicenseConfigurationSource
        _licenseSource;

    private readonly ILicenseSignatureVerifier
        _signatureVerifier;

    private readonly IHardwareIdProvider
        _hardwareIdProvider;

    private readonly TimeProvider
        _timeProvider;

    /*
     * Only cryptographically verified and fully validated claims
     * are stored here.
     */
    private VerifiedLicenseClaims?
        _activeLicense;

    /// <summary>
    /// Compatibility constructor.
    ///
    /// Existing application construction can continue compiling
    /// until the concrete Infrastructure licensing dependencies
    /// are connected.
    ///
    /// This configuration deliberately fails closed.
    /// </summary>
    public OfflineLicenseManager()
        : this(
            new UnconfiguredLicenseConfigurationSource(),
            new UnconfiguredLicenseSignatureVerifier(),
            new UnconfiguredHardwareIdProvider(),
            TimeProvider.System
        )
    {
    }

    /// <summary>
    /// Creates a configured offline-license manager.
    /// </summary>
    public OfflineLicenseManager(
        ILicenseConfigurationSource licenseSource,
        ILicenseSignatureVerifier signatureVerifier,
        IHardwareIdProvider hardwareIdProvider,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(
            licenseSource
        );

        ArgumentNullException.ThrowIfNull(
            signatureVerifier
        );

        ArgumentNullException.ThrowIfNull(
            hardwareIdProvider
        );

        _licenseSource =
            licenseSource;

        _signatureVerifier =
            signatureVerifier;

        _hardwareIdProvider =
            hardwareIdProvider;

        _timeProvider =
            timeProvider ??
            TimeProvider.System;
    }

    /// <summary>
    /// Loads and validates the kiosk's signed offline license.
    ///
    /// Any failure clears the active license and acts as a hard
    /// stop for licensed hardware initialization.
    /// </summary>
    public async Task LoadAndValidateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * Fail closed.
         *
         * A previous license must not remain trusted while a new
         * validation attempt is in progress.
         */
        _activeLicense =
            null;

        var persistedLicense =
            await _licenseSource
                .LoadAsync(
                    cancellationToken
                )
                .ConfigureAwait(false);

        if (persistedLicense is null)
        {
            throw new LicenseValidationException(
                "No offline license is available for this kiosk."
            );
        }

        ValidatePersistedRecord(
            persistedLicense
        );

        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * =====================================================
         * STEP 1 — SIGNATURE
         * =====================================================
         *
         * Claims returned here are the trusted values protected
         * by the asymmetric signature.
         */
        var verifiedClaims =
            VerifySignedToken(
                persistedLicense.SignedToken
            );

        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * =====================================================
         * STEP 2 — EXPIRATION
         * =====================================================
         */
        ValidateExpiration(
            verifiedClaims
        );

        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * =====================================================
         * STEP 3 — HARDWARE ID NODE LOCK
         * =====================================================
         */
        ValidateHardwareId(
            verifiedClaims
        );

        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * =====================================================
         * STEP 4 — TIER CONSISTENCY
         * =====================================================
         */
        ValidateTier(
            verifiedClaims
        );

        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * Trust boundary.
         *
         * Only after every validation stage succeeds may the
         * signed claims become the active runtime license.
         */
        _activeLicense =
            verifiedClaims;
    }

    /// <summary>
    /// Ensures that the active validated license permits the
    /// requested runtime feature.
    /// </summary>
    public void EnforceFeatureAccess(
        LicensedFeature feature)
    {
        if (!Enum.IsDefined(feature))
        {
            throw new ArgumentOutOfRangeException(
                nameof(feature),
                feature,
                "The licensed feature is not supported."
            );
        }

        var license =
            _activeLicense ??
            throw new LicenseValidationException(
                "No validated offline license is active."
            );

        var permitted =
            feature switch
            {
                LicensedFeature.CashRecycler =>
                    IsCashRecyclerPermitted(
                        license
                    ),

                LicensedFeature.AiCameraTelemetry =>
                    IsAiTelemetryPermitted(
                        license
                    ),

                LicensedFeature.CustomErpSync =>
                    IsCustomErpSyncPermitted(
                        license
                    ),

                _ =>
                    false
            };

        if (!permitted)
        {
            throw new LicenseValidationException(
                $"The active {license.Tier} license does not " +
                $"permit the {feature} feature."
            );
        }
    }

    /// <summary>
    /// Performs structural validation of the persisted record.
    ///
    /// The persistence fields are not trusted as signed claims;
    /// only SignedToken is used to establish runtime trust.
    /// </summary>
    private static void ValidatePersistedRecord(
        LicenseConfiguration license)
    {
        if (
            string.IsNullOrWhiteSpace(
                license.SignedToken
            )
        )
        {
            throw new LicenseValidationException(
                "The offline license does not contain a signed token."
            );
        }
    }

    /// <summary>
    /// Cryptographically verifies the token and obtains the
    /// claims protected by that signature.
    /// </summary>
    private VerifiedLicenseClaims
        VerifySignedToken(
            string signedToken)
    {
        VerifiedLicenseClaims?
            claims;

        try
        {
            claims =
                _signatureVerifier
                    .VerifyAndRead(
                        signedToken
                    );
        }
        catch (
            LicenseValidationException
        )
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LicenseValidationException(
                "The offline-license signature could not be verified.",
                exception
            );
        }

        if (claims is null)
        {
            throw new LicenseValidationException(
                "The offline-license signature is invalid."
            );
        }

        return claims;
    }

    /// <summary>
    /// Rejects an expired license.
    ///
    /// A license expiring exactly at the current UTC instant is
    /// considered expired.
    /// </summary>
    private void ValidateExpiration(
        VerifiedLicenseClaims license)
    {
        var nowUtc =
            _timeProvider
                .GetUtcNow();

        if (
            license.ExpiresAtUtc <=
            nowUtc
        )
        {
            throw new LicenseValidationException(
                "The offline license has expired."
            );
        }
    }

    /// <summary>
    /// Validates that the HardwareId protected by the signed
    /// token matches the current machine.
    /// </summary>
    private void ValidateHardwareId(
        VerifiedLicenseClaims license)
    {
        if (
            string.IsNullOrWhiteSpace(
                license.HardwareId
            )
        )
        {
            throw new LicenseValidationException(
                "The signed license does not contain a HardwareId."
            );
        }

        string localHardwareId;

        try
        {
            localHardwareId =
                _hardwareIdProvider
                    .GetHardwareId();
        }
        catch (
            LicenseValidationException
        )
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LicenseValidationException(
                "The kiosk HardwareId could not be determined.",
                exception
            );
        }

        if (
            string.IsNullOrWhiteSpace(
                localHardwareId
            )
        )
        {
            throw new LicenseValidationException(
                "The kiosk HardwareId could not be determined."
            );
        }

        /*
         * Hardware identifiers are opaque deterministic values.
         *
         * Ordinal comparison avoids culture-sensitive behavior.
         */
        if (
            !string.Equals(
                license.HardwareId.Trim(),
                localHardwareId.Trim(),
                StringComparison.Ordinal
            )
        )
        {
            throw new LicenseValidationException(
                "The offline license is not valid for this kiosk."
            );
        }
    }

    /// <summary>
    /// Validates that the signed feature and node claims are
    /// consistent with the selected tier.
    /// </summary>
    private static void ValidateTier(
        VerifiedLicenseClaims license)
    {
        if (
            !Enum.IsDefined(
                license.Tier
            )
        )
        {
            throw new LicenseValidationException(
                "The offline license contains an unsupported tier."
            );
        }

        switch (license.Tier)
        {
            case LicenseTier.Lite:
                {
                    ValidateLite(
                        license
                    );

                    break;
                }

            case LicenseTier.Pro:
                {
                    ValidatePro(
                        license
                    );

                    break;
                }

            case LicenseTier.Enterprise:
                {
                    ValidateEnterprise(
                        license
                    );

                    break;
                }

            default:
                {
                    throw new LicenseValidationException(
                        "The offline license contains an unsupported tier."
                    );
                }
        }
    }

    /// <summary>
    /// Lite tier:
    ///
    /// - maximum two kiosk nodes;
    /// - cash module forced off;
    /// - AI telemetry forced off;
    /// - custom ERP synchronization unavailable.
    /// </summary>
    private static void ValidateLite(
        VerifiedLicenseClaims license)
    {
        ValidatePositiveKioskCount(
            license
        );

        if (
            license.MaxKiosks >
            2
        )
        {
            throw new LicenseValidationException(
                "A Lite license cannot permit more than two kiosks."
            );
        }

        if (
            license.CashModuleEnabled
        )
        {
            throw new LicenseValidationException(
                "A Lite license cannot enable the cash module."
            );
        }

        if (
            license.AiModuleEnabled
        )
        {
            throw new LicenseValidationException(
                "A Lite license cannot enable AI camera telemetry."
            );
        }

        if (
            license.ErpSyncEnabled
        )
        {
            throw new LicenseValidationException(
                "A Lite license cannot enable custom ERP synchronization."
            );
        }
    }

    /// <summary>
    /// Pro tier:
    ///
    /// - maximum five kiosk nodes;
    /// - cash may be enabled;
    /// - AI may be enabled;
    /// - custom ERP synchronization unavailable.
    /// </summary>
    private static void ValidatePro(
        VerifiedLicenseClaims license)
    {
        ValidatePositiveKioskCount(
            license
        );

        if (
            license.MaxKiosks >
            5
        )
        {
            throw new LicenseValidationException(
                "A Pro license cannot permit more than five kiosks."
            );
        }

        if (
            license.ErpSyncEnabled
        )
        {
            throw new LicenseValidationException(
                "A Pro license cannot enable custom ERP synchronization."
            );
        }
    }

    /// <summary>
    /// Enterprise tier:
    ///
    /// - no upper node bracket is enforced;
    /// - cash may be enabled;
    /// - AI may be enabled;
    /// - custom ERP synchronization may be enabled.
    /// </summary>
    private static void ValidateEnterprise(
        VerifiedLicenseClaims license)
    {
        ValidatePositiveKioskCount(
            license
        );
    }

    /// <summary>
    /// All tiers must permit at least one kiosk.
    /// </summary>
    private static void ValidatePositiveKioskCount(
        VerifiedLicenseClaims license)
    {
        if (
            license.MaxKiosks <=
            0
        )
        {
            throw new LicenseValidationException(
                "The offline license must permit at least one kiosk."
            );
        }
    }

    private static bool IsCashRecyclerPermitted(
        VerifiedLicenseClaims license)
    {
        return
            license.Tier !=
                LicenseTier.Lite &&
            license.CashModuleEnabled;
    }

    private static bool IsAiTelemetryPermitted(
        VerifiedLicenseClaims license)
    {
        return
            license.Tier !=
                LicenseTier.Lite &&
            license.AiModuleEnabled;
    }

    private static bool IsCustomErpSyncPermitted(
        VerifiedLicenseClaims license)
    {
        return
            license.Tier ==
                LicenseTier.Enterprise &&
            license.ErpSyncEnabled;
    }

    // =========================================================
    // FAIL-CLOSED COMPATIBILITY IMPLEMENTATIONS
    // =========================================================

    /// <summary>
    /// Keeps the current parameterless construction path
    /// compilable until Infrastructure persistence wiring is
    /// connected.
    ///
    /// It never grants a license.
    /// </summary>
    private sealed class
        UnconfiguredLicenseConfigurationSource
        : ILicenseConfigurationSource
    {
        public Task<LicenseConfiguration?>
            LoadAsync(
                CancellationToken cancellationToken =
                    default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.FromResult<
                LicenseConfiguration?
            >(
                null
            );
        }
    }

    /// <summary>
    /// Fail-closed placeholder until the real asymmetric token
    /// verifier is connected.
    /// </summary>
    private sealed class
        UnconfiguredLicenseSignatureVerifier
        : ILicenseSignatureVerifier
    {
        public VerifiedLicenseClaims?
            VerifyAndRead(
                string signedToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                signedToken
            );

            throw new LicenseValidationException(
                "The offline-license signature verifier has not " +
                "been configured."
            );
        }
    }

    /// <summary>
    /// Fail-closed placeholder until Infrastructure provides the
    /// deterministic machine HardwareId.
    /// </summary>
    private sealed class
        UnconfiguredHardwareIdProvider
        : IHardwareIdProvider
    {
        public string GetHardwareId()
        {
            throw new LicenseValidationException(
                "The kiosk HardwareId provider has not been configured."
            );
        }
    }
}