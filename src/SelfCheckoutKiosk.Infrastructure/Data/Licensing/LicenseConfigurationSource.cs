using Microsoft.EntityFrameworkCore;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Entities;

namespace SelfCheckoutKiosk.Infrastructure.Data.Licensing;

/// <summary>
/// Loads the kiosk's persisted offline-license configuration
/// from the local KioskDbContext.
///
/// This class performs persistence access only.
/// Cryptographic verification, expiration checking,
/// HardwareId validation, and feature enforcement belong to
/// OfflineLicenseManager in Core.
/// </summary>
public sealed class LicenseConfigurationSource
    : ILicenseConfigurationSource
{
    private readonly KioskDbContext
        _dbContext;

    public LicenseConfigurationSource(
        KioskDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(
            dbContext
        );

        _dbContext =
            dbContext;
    }

    /// <summary>
    /// Loads the single locally provisioned license record.
    ///
    /// No record:
    ///     returns null.
    ///
    /// More than one record:
    ///     fails closed because the kiosk must not arbitrarily
    ///     choose between conflicting license configurations.
    /// </summary>
    public async Task<LicenseConfiguration?>
        LoadAsync(
            CancellationToken cancellationToken =
                default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        return await _dbContext
            .LicenseConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}