namespace SelfCheckoutKiosk.Core.Licensing;

/// <summary>
/// Verifies an asymmetrically signed offline-license token and
/// returns the claims protected by that signature.
///
/// Returning null means the token or signature is invalid.
///
/// Only the public verification key may exist on the kiosk.
/// The private signing key must never be stored locally.
/// </summary>
public interface ILicenseSignatureVerifier
{
    /// <summary>
    /// Verifies the signed token and returns its trusted claims.
    /// </summary>
    VerifiedLicenseClaims? VerifyAndRead(
        string signedToken
    );
}