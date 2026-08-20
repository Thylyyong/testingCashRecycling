using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.App.Composition;

public sealed record KioskTestPackageConfiguration
{
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = "Development";

    [JsonPropertyName("licenseMode")]
    public string LicenseMode { get; init; } = "Development";

    [JsonPropertyName("cashHardwareMode")]
    public string CashHardwareMode { get; init; } = "Physical";

    [JsonPropertyName("developmentPublicKeyRelativePath")]
    public string? DevelopmentPublicKeyRelativePath { get; init; }

    [JsonPropertyName("cashDevice")]
    public KioskTestCashDeviceConfiguration CashDevice { get; init; } = new();

    public static KioskTestPackageConfiguration? TryLoad(string applicationBasePath)
    {
        string[] candidatePaths =
        {
            Path.Combine(applicationBasePath, "Config", "kiosk-test.json"),
            Path.Combine(applicationBasePath, "kiosk-test.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "kiosk-test.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kiosk-test.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Config", "kiosk-test.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "kiosk-test.json"),
            Path.Combine(applicationBasePath, "..", "..", "..", "..", "src", "SelfCheckoutKiosk.App", "Config", "kiosk-test.json")
        };

        foreach (var path in candidatePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<KioskTestPackageConfiguration>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (config != null)
                    {
                        return config;
                    }
                }
            }
            catch
            {
                // Ignore and try next candidate path
            }
        }

        return null;
    }
}

public sealed record KioskTestCashDeviceConfiguration
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = "http://127.0.0.1:5000/";

    [JsonPropertyName("username")]
    public string Username { get; init; } = "admin";

    [JsonPropertyName("password")]
    public string Password { get; init; } = "password";

    [JsonPropertyName("comPort")]
    public string ComPort { get; init; } = "AUTO";

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "USD,KHR";

    [JsonPropertyName("sspAddress")]
    public int SspAddress { get; init; } = 0;

    [JsonPropertyName("encryptionKey")]
    public ulong? EncryptionKey { get; init; }

    [JsonPropertyName("pollIntervalMs")]
    public int PollIntervalMilliseconds { get; init; } = 200;

    [JsonPropertyName("requestTimeoutMs")]
    public int RequestTimeoutMilliseconds { get; init; } = 5000;

    [JsonPropertyName("maximumPollFailures")]
    public int MaximumPollFailures { get; init; } = 3;
}
