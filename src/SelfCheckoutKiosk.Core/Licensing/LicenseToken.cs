using System.Text.Json.Serialization;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Licensing;

/// <summary>The signed payload — everything the license actually claims.</summary>
public sealed record LicensePayload(
    [property: JsonPropertyName("hardwareId")] string HardwareId,
    [property: JsonPropertyName("tier")] LicenseTier Tier,
    [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc,
    [property: JsonPropertyName("maxKiosks")] int MaxKiosks,
    [property: JsonPropertyName("aiEnabled")] bool AiEnabled);

/// <summary>On-disk token shape: the payload plus its detached signature,
/// both base64. The payload is signed as raw UTF-8 JSON bytes — verify against
/// exactly the bytes on disk, never a re-serialized copy.</summary>
public sealed record SignedLicenseToken(
    [property: JsonPropertyName("payload")] string PayloadBase64,
    [property: JsonPropertyName("signature")] string SignatureBase64);

/// <summary>
/// Source-generated System.Text.Json context — AOT/trim-safe (no reflection
/// -based serialization) per the project's Native AOT mandate.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LicensePayload))]
[JsonSerializable(typeof(SignedLicenseToken))]
internal sealed partial class LicenseJsonContext : JsonSerializerContext;
