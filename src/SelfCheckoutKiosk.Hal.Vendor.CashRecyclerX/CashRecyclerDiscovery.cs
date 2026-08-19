using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>Read-only discovery for a cash recycler already exposed by the vendor REST service.</summary>
public static class CashRecyclerDiscovery
{
    /// <summary>
    /// Queries device status for each supplied port. This never opens a serial port,
    /// enables the acceptor, or sends a cash-handling command.
    /// </summary>
    public static async Task<string?> FindConnectedPortAsync(
        CashRecyclerOptions options,
        IEnumerable<string> candidatePorts,
        CancellationToken cancellationToken = default)
    {
        options.ValidateForDiscovery();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var tokenHandler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Expires = DateTime.UtcNow.AddMinutes(5),
            Issuer = options.JwtIssuer,
            Audience = options.JwtAudience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.ApiKey!)),
                SecurityAlgorithms.HmacSha256Signature)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor)));

        string baseUrl = options.ApiBaseUrl.TrimEnd('/');
        foreach (string port in candidatePorts.OrderBy(static port => port, StringComparer.OrdinalIgnoreCase))
        {
            string deviceId = $"NOTE_VALIDATOR-{port}";
            string endpoint = $"{baseUrl}/api/CashDevice/GetDeviceStatus?deviceID={Uri.EscapeDataString(deviceId)}";
            try
            {
                using var response = await client.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(json) && json != "[]")
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        
                        if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            if (root.GetArrayLength() > 0)
                                return port;
                        }
                        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            string model = "";
                            if (root.TryGetProperty("DeviceModel", out var m1)) model = m1.GetString() ?? "";
                            else if (root.TryGetProperty("deviceModel", out var m2)) model = m2.GetString() ?? "";
                            else if (root.TryGetProperty("model", out var m3)) model = m3.GetString() ?? "";

                            bool isOpen = false;
                            if (root.TryGetProperty("IsOpen", out var io)) isOpen = io.GetBoolean();
                            else if (root.TryGetProperty("isOpen", out var io2)) isOpen = io2.GetBoolean();

                            if (isOpen && !string.IsNullOrEmpty(model) && !model.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) && !model.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                            {
                                return port;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Try the next port. The caller reports that no verified device was found.
            }
        }

        return null;
    }
}
