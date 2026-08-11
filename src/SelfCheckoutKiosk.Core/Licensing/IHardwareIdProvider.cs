namespace SelfCheckoutKiosk.Core.Licensing;

/// <summary>
/// Provides the stable machine identity used to node-lock the
/// kiosk's offline license.
///
/// The concrete Windows/TPM implementation belongs to
/// SelfCheckoutKiosk.Infrastructure.
/// </summary>
public interface IHardwareIdProvider
{
    /// <summary>
    /// Returns the stable identifier for the current kiosk.
    /// </summary>
    string GetHardwareId();
}