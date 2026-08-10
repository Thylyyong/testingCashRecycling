namespace SelfCheckoutKiosk.Infrastructure.Security;

/// <summary>
/// Provides the public portion of the persistent TPM-backed
/// identity key used to derive the kiosk HardwareId.
///
/// The private key must never leave the TPM-backed key-storage
/// provider.
/// </summary>
public interface ITpmIdentityKeyStore
{
    /// <summary>
    /// Opens the existing TPM identity key or creates it when
    /// it has not yet been provisioned, then returns only its
    /// public-key blob.
    /// </summary>
    byte[] GetOrCreatePublicKeyBlob();
}