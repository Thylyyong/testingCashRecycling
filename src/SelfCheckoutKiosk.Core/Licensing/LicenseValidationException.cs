namespace SelfCheckoutKiosk.Core.Licensing;

/// <summary>
/// Represents a hard-stop offline-license validation or
/// feature-enforcement failure.
/// </summary>
public sealed class LicenseValidationException
    : InvalidOperationException
{
    public LicenseValidationException(
        string message)
        : base(
            message
        )
    {
    }

    public LicenseValidationException(
        string message,
        Exception innerException)
        : base(
            message,
            innerException
        )
    {
    }
}