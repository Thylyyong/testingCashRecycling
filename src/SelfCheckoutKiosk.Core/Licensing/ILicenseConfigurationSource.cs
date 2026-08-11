using SelfCheckoutKiosk.Domain.Entities;

namespace SelfCheckoutKiosk.Core.Licensing;

/// <summary>
/// Loads the locally persisted offline-license configuration.
///
/// Core does not know whether the license is stored in EF Core,
/// a file, or another Infrastructure persistence mechanism.
/// </summary>
public interface ILicenseConfigurationSource
{
    /// <summary>
    /// Loads the current kiosk license.
    ///
    /// Returns null when no license has been provisioned.
    /// </summary>
    Task<LicenseConfiguration?> LoadAsync(
        CancellationToken cancellationToken = default
    );
}