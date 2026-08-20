using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(AuthenticateRequest))]
[JsonSerializable(typeof(AuthenticateResponse))]
[JsonSerializable(typeof(RestErrorResponse))]
[JsonSerializable(typeof(OpenConnectionRequest))]
[JsonSerializable(typeof(OpenConnectionResponse))]
[JsonSerializable(typeof(DeviceStatusItemResponse[]))]
internal partial class CashDeviceJsonContext : JsonSerializerContext;
