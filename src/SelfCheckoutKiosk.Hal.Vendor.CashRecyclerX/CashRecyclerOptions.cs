namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Machine-specific settings for the cash-recycler REST adapter.
/// Configure these with environment variables; do not commit credentials or
/// workstation COM-port assignments to source control.
/// </summary>
public sealed class CashRecyclerOptions
{
    public string ApiBaseUrl { get; init; } = "http://localhost:5000";
    public string? ComPort { get; init; }
    public string? JwtIssuer { get; init; }
    public string? JwtAudience { get; init; }
    public string? ApiKey { get; init; }
    public bool UseRealApi { get; init; }

    /// <summary>
    /// Loads machine-specific values from the process environment.
    /// Real hardware is intentionally opt-in.
    /// </summary>
    public static CashRecyclerOptions FromEnvironment(string? apiKey = null) => new()
    {
        ApiBaseUrl = Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_API_BASE_URL")
            ?? "http://localhost:5000",
        ComPort = Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_COM_PORT"),
        // Default to INNOVATIVETECHNOLOGY so no env vars are needed
        JwtIssuer   = Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_JWT_ISSUER")
            ?? "INNOVATIVETECHNOLOGY",
        JwtAudience = Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_JWT_AUDIENCE")
            ?? "INNOVATIVETECHNOLOGY",
        ApiKey = apiKey ?? Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_API_KEY"),
        // Default to true — hardware is always attempted unless explicitly disabled
        UseRealApi = !bool.TryParse(
            Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_USE_REAL_API"),
            out var useRealApi) || useRealApi
    };

    internal void ValidateForRealHardware()
    {
        ValidateForDiscovery();
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Cash recycler API base URL must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(ComPort))
            throw new InvalidOperationException("Set SELFCHECKOUT_CASH_RECYCLER_COM_PORT before enabling real hardware.");
        if (!ComPort.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(ComPort[3..], out var portNumber) || portNumber < 1)
            throw new InvalidOperationException("Cash recycler COM port must be in the form COM1, COM2, and so on.");
    }

    internal void ValidateForDiscovery()
    {
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Cash recycler API base URL must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Set the cash recycler API key before discovering real hardware.");
        if (string.IsNullOrWhiteSpace(JwtIssuer) || string.IsNullOrWhiteSpace(JwtAudience))
            throw new InvalidOperationException("Set the cash recycler JWT issuer and audience before discovering real hardware.");
    }
}
