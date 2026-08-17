namespace SelfCheckoutKiosk.Infrastructure.Security;

/// <summary>
/// Supplies stable TPM-backed machine identity material to
/// HardwareIdProvider.
///
/// The material is derived only from the public portion of the
/// persistent TPM-backed identity key.
///
/// HardwareIdProvider performs the final SHA-256 transformation
/// into the externally used kiosk HardwareId.
/// </summary>
public sealed class WindowsTpmHardwareIdentityMaterialSource
    : IHardwareIdentityMaterialSource
{
    private readonly ITpmIdentityKeyStore
        _keyStore;

    /// <summary>
    /// Creates the production Windows TPM implementation.
    /// </summary>
    public WindowsTpmHardwareIdentityMaterialSource()
        : this(
            new WindowsTpmIdentityKeyStore()
        )
    {
    }

    /// <summary>
    /// Creates the source with an injected key store.
    ///
    /// Primarily useful for deterministic Infrastructure tests.
    /// </summary>
    public WindowsTpmHardwareIdentityMaterialSource(
        ITpmIdentityKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(
            keyStore
        );

        _keyStore =
            keyStore;
    }

    /// <summary>
    /// Returns deterministic textual identity material derived
    /// from the TPM-backed public key.
    /// </summary>
    public string GetIdentityMaterial()
    {
        byte[] publicKeyBlob;

        try
        {
            publicKeyBlob =
                _keyStore
                    .GetOrCreatePublicKeyBlob();
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
                "The TPM-backed kiosk identity could not be read.",
                exception
            );
        }

        if (
            publicKeyBlob is
                null ||
            publicKeyBlob.Length ==
                0
        )
        {
            throw new HardwareIdentityException(
                "The TPM-backed kiosk identity contains no " +
                "public-key material."
            );
        }

        /*
         * Base64 provides a deterministic textual
         * representation of the public CNG blob.
         *
         * HardwareIdProvider hashes this value before exposing
         * the final node identifier.
         */
        return Convert.ToBase64String(
            publicKeyBlob
        );
    }
}