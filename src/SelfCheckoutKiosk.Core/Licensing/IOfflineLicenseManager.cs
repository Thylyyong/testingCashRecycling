namespace SelfCheckoutKiosk.Core.Licensing;

/// <summary>
/// Contract for loading, validating, and enforcing the kiosk's
/// offline license.
///
/// LLCoreLogicEngine depends on this abstraction so licensing can
/// be replaced with deterministic fakes during unit testing.
/// </summary>
public interface IOfflineLicenseManager
{
    /// <summary>
    /// Loads and validates the kiosk license.
    ///
    /// Validation failure must prevent hardware initialization.
    /// </summary>
    Task LoadAndValidateAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Ensures that the active validated license permits the
    /// requested runtime feature.
    /// </summary>
    void EnforceFeatureAccess(
        LicensedFeature feature
    );
}