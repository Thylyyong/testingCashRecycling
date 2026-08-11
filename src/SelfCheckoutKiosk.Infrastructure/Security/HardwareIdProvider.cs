using System.Security.Cryptography;
using System.Text;
using SelfCheckoutKiosk.Core.Licensing;

namespace SelfCheckoutKiosk.Infrastructure.Security;

/// <summary>
/// Produces the stable node identifier used by the offline
/// licensing system.
///
/// Raw machine identity material is never exposed outside this
/// class. The material is converted into a deterministic SHA-256
/// identifier before being returned to Core.
/// </summary>
public sealed class HardwareIdProvider
    : IHardwareIdProvider
{
    private const string HardwareIdPrefix =
        "KIOSK-";

    private readonly IHardwareIdentityMaterialSource
        _identityMaterialSource;

    /// <summary>
    /// Compatibility constructor.
    ///
    /// Until the approved Windows/TPM identity-material source is
    /// connected, this constructor deliberately fails closed when
    /// GetHardwareId is called.
    /// </summary>
    public HardwareIdProvider()
        : this(
            new UnconfiguredHardwareIdentityMaterialSource()
        )
    {
    }

    /// <summary>
    /// Creates a HardwareIdProvider using the supplied machine
    /// identity-material source.
    /// </summary>
    public HardwareIdProvider(
        IHardwareIdentityMaterialSource identityMaterialSource)
    {
        ArgumentNullException.ThrowIfNull(
            identityMaterialSource
        );

        _identityMaterialSource =
            identityMaterialSource;
    }

    /// <summary>
    /// Returns the deterministic HardwareId for this kiosk.
    ///
    /// Format:
    ///
    /// KIOSK-{SHA256}
    /// </summary>
    public string GetHardwareId()
    {
        string identityMaterial;

        try
        {
            identityMaterial =
                _identityMaterialSource
                    .GetIdentityMaterial();
        }
        catch (
            HardwareIdentityException
        )
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new HardwareIdentityException(
                "The kiosk hardware identity material could not be read.",
                exception
            );
        }

        if (
            string.IsNullOrWhiteSpace(
                identityMaterial
            )
        )
        {
            throw new HardwareIdentityException(
                "The kiosk hardware identity material is empty."
            );
        }

        /*
         * Remove accidental outer whitespace while preserving the
         * actual machine identity value.
         */
        var normalizedMaterial =
            identityMaterial.Trim();

        var materialBytes =
            Encoding.UTF8.GetBytes(
                normalizedMaterial
            );

        var hash =
            SHA256.HashData(
                materialBytes
            );

        return
            HardwareIdPrefix +
            Convert.ToHexString(
                hash
            );
    }

    /// <summary>
    /// Fail-closed placeholder until the actual Windows/TPM
    /// material source is implemented.
    /// </summary>
    private sealed class
        UnconfiguredHardwareIdentityMaterialSource
        : IHardwareIdentityMaterialSource
    {
        public string GetIdentityMaterial()
        {
            throw new HardwareIdentityException(
                "The Windows/TPM hardware identity source has not " +
                "been configured."
            );
        }
    }
}