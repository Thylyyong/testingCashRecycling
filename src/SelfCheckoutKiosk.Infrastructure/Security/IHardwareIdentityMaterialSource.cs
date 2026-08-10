namespace SelfCheckoutKiosk.Infrastructure.Security;

/// <summary>
/// Supplies stable machine-specific identity material.
///
/// The final production implementation should obtain this
/// material from the approved Windows/TPM-backed mechanism.
///
/// This abstraction keeps HardwareIdProvider deterministic and
/// independently testable.
/// </summary>
public interface IHardwareIdentityMaterialSource
{
    /// <summary>
    /// Returns stable identity material for the current kiosk.
    ///
    /// The returned value must remain stable for the lifetime of
    /// the kiosk installation.
    /// </summary>
    string GetIdentityMaterial();
}