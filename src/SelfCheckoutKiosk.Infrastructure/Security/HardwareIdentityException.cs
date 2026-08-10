namespace SelfCheckoutKiosk.Infrastructure.Security;

/// <summary>
/// Represents a failure while obtaining or generating the
/// kiosk's stable hardware identity.
/// </summary>
public sealed class HardwareIdentityException
    : InvalidOperationException
{
    public HardwareIdentityException(
        string message)
        : base(
            message
        )
    {
    }

    public HardwareIdentityException(
        string message,
        Exception innerException)
        : base(
            message,
            innerException
        )
    {
    }
}