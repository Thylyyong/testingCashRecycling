namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Process and readiness settings for the separately distributed ITL REST host.
/// </summary>
public sealed class CashDeviceServerOptions
{
    public required Uri BaseUri { get; init; }
    public required string ExecutablePath { get; init; }
    public required string Version { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ProbeRequestTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(BaseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);

        if (!BaseUri.IsAbsoluteUri ||
            (BaseUri.Scheme != Uri.UriSchemeHttp &&
             BaseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "CashDevice server BaseUri must be an absolute HTTP(S) URI.",
                nameof(BaseUri));
        }

        ValidatePositive(StartupTimeout, nameof(StartupTimeout));
        ValidatePositive(ProbeInterval, nameof(ProbeInterval));
        ValidatePositive(ProbeRequestTimeout, nameof(ProbeRequestTimeout));
        ValidatePositive(ShutdownTimeout, nameof(ShutdownTimeout));
    }

    private static void ValidatePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                name,
                "CashDevice server timing values must be greater than zero.");
        }
    }
}
