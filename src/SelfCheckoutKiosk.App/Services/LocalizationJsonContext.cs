using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.App.Services
{
    [JsonSerializable(typeof(Dictionary<string, string>))]
    public partial class LocalizationJsonContext : JsonSerializerContext
    {
    }
}