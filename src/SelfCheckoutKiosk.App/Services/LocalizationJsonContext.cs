using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.App.Services
{
    [JsonSourceGenerationOptions(
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true)]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    public partial class LocalizationJsonContext : JsonSerializerContext
    {
    }
}