using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.Infrastructure.Security.Licensing;

/// <summary>
/// Source-generated JSON metadata used to deserialize the
/// offline-license token payload.
///
/// Source generation keeps the token parsing path deterministic
/// and avoids runtime reflection-based metadata discovery.
/// </summary>
[JsonSourceGenerationOptions(
    GenerationMode =
        JsonSourceGenerationMode.Metadata
)]
[JsonSerializable(
    typeof(LicenseTokenPayload)
)]
internal sealed partial class LicenseTokenJsonContext
    : JsonSerializerContext
{
}