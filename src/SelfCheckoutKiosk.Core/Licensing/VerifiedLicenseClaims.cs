using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Licensing;

/// <summary>
/// Trusted claims extracted from a cryptographically verified
/// offline-license token.
///
/// OfflineLicenseManager must enforce these signed claims rather
/// than trusting editable persistence fields independently.
/// </summary>
public sealed record VerifiedLicenseClaims(
    string HardwareId,
    LicenseTier Tier,
    int MaxKiosks,
    bool CashModuleEnabled,
    bool AiModuleEnabled,
    bool ErpSyncEnabled,
    DateTimeOffset ExpiresAtUtc
);