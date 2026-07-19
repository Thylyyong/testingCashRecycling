namespace SelfCheckoutKiosk.Core.Licensing;

public enum LicensedFeature { CashRecycler, AiCameraTelemetry, CustomErpSync }

/// <summary>
/// STUB — offline license gate (Blueprint §3). Verifies a signed token
/// (public key embedded in the AOT binary; private signing key held ONLY by
/// the licensing service, owned by the Lead). Exposes a RUNTIME gate that
/// every gated subsystem must call before initialising — not just at startup.
///
/// SECURITY BOUNDARY: the Lead injects cryptographic material; developers code
/// against this type and never hold the private key.
///
/// TODO(Lead): implement signature -> expiry -> node-lock -> tier validation.
/// </summary>
public sealed class OfflineLicenseManager
{
    public Task LoadAndValidateAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Lead): implement token validation. Failure = hard stop.");

    public void EnforceFeatureAccess(LicensedFeature feature)
        => throw new NotImplementedException("TODO(Lead): throw if the active tier does not license the feature.");
}
