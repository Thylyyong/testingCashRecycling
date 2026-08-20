using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

internal sealed record AuthenticateRequest(
    [property: JsonPropertyName("Username")] string Username,
    [property: JsonPropertyName("Password")] string Password);

internal sealed record AuthenticateResponse(
    [property: JsonPropertyName("token")] string? Token);

internal sealed record RestErrorResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("openResult")]
    public string? OpenResult { get; init; }
}

internal sealed record OpenConnectionRequest
{
    [JsonPropertyName("ComPort")]
    public required string ComPort { get; init; }

    [JsonPropertyName("SspAddress")]
    public int SspAddress { get; init; }

    [JsonPropertyName("EncKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? EncryptionKey { get; init; }

    [JsonPropertyName("LogFilePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogFilePath { get; init; }

    [JsonPropertyName("EnableAcceptor")]
    public bool EnableAcceptor { get; init; }

    [JsonPropertyName("EnableAutoAcceptEscrow")]
    public bool EnableAutoAcceptEscrow { get; init; }

    [JsonPropertyName("EnablePayout")]
    public bool EnablePayout { get; init; }
}

internal sealed record OpenConnectionResponse
{
    [JsonPropertyName("deviceID")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("isOpen")]
    public bool? IsOpen { get; init; }

    [JsonPropertyName("deviceModel")]
    public string? DeviceModel { get; init; }

    [JsonPropertyName("deviceError")]
    public string? DeviceError { get; init; }

    [JsonPropertyName("sspProtocolVersion")]
    public int? SspProtocolVersion { get; init; }

    [JsonPropertyName("firmware")]
    public string? Firmware { get; init; }

    [JsonPropertyName("dataset")]
    public string? Dataset { get; init; }

    [JsonPropertyName("acceptorEnabled")]
    public bool? AcceptorEnabled { get; init; }

    [JsonPropertyName("autoAcceptEscrowEnabled")]
    public bool? AutoAcceptEscrowEnabled { get; init; }

    [JsonPropertyName("payoutEnabled")]
    public bool? PayoutEnabled { get; init; }

    [JsonPropertyName("openResult")]
    public string? OpenResult { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed record DeviceStatusItemResponse
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("stateAsString")]
    public string? StateAsString { get; init; }

    [JsonPropertyName("isRunning")]
    public bool? IsRunning { get; init; }

    [JsonPropertyName("eventTypeAsString")]
    public string? EventTypeAsString { get; init; }

    [JsonPropertyName("value")]
    public decimal? Value { get; init; }

    [JsonPropertyName("value2")]
    public decimal? Value2 { get; init; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
