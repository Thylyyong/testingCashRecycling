using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.Infrastructure.Security.Licensing;

/// <summary>
/// Serialized payload contained inside an offline-license token.
///
/// This object is untrusted until the signature protecting the
/// payload has been successfully verified.
/// </summary>
internal sealed class LicenseTokenPayload
{
    [JsonPropertyName("hardwareId")]
    public string? HardwareId
    {
        get;
        init;
    }

    [JsonPropertyName("tier")]
    public string? Tier
    {
        get;
        init;
    }

    [JsonPropertyName("maxKiosks")]
    public int MaxKiosks
    {
        get;
        init;
    }

    [JsonPropertyName("cashModuleEnabled")]
    public bool CashModuleEnabled
    {
        get;
        init;
    }

    [JsonPropertyName("aiModuleEnabled")]
    public bool AiModuleEnabled
    {
        get;
        init;
    }

    [JsonPropertyName("erpSyncEnabled")]
    public bool ErpSyncEnabled
    {
        get;
        init;
    }

    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset ExpiresAtUtc
    {
        get;
        init;
    }
}