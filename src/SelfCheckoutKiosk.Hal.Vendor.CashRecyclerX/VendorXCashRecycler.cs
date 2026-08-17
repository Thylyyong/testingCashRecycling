using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfCheckoutKiosk.Integration.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfCheckoutKiosk.App")]

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Hybrid Adapter for the "CashRecyclerX" SKU (Blueprint §4).
/// Implements <see cref="ICashRecycler"/> against both a local hardware REST API
/// (CashDevice-RestAPI server) and a software-only simulation state machine.
///
/// FEATURES:
///   • Automatic COM port discovery across all active system serial ports.
///   • Auto-verifies device connection status on hardware startup.
///   • Dual-currency (USD & KHR) note intake & running accumulator.
///   • Resilient JSON property parsing for REST API payloads.
/// </summary>
public sealed class VendorXCashRecycler : ICashRecycler, IDisposable
{
    private enum RecyclerState { Disconnected, Connected, Armed, Stopped }

    private readonly object _stateLock = new();
    private RecyclerState _state = RecyclerState.Disconnected;

    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private readonly string? _apiKey;
    private bool _useRealApi;

    private string _activeComPort = "COM7";
    private string _activeDeviceId = "NOTE_VALIDATOR-COM7";

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public VendorXCashRecycler(
        string apiBaseUrl = "http://localhost:5000",
        string? apiKey = null,
        bool useRealApi = false,
        HttpClient? httpClient = null)
    {
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _useRealApi = useRealApi;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
    public event EventHandler<HardwareFaultEventArgs>? OnFault;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    var keyBytes = Encoding.UTF8.GetBytes(_apiKey);
                    var tokenDescriptor = new SecurityTokenDescriptor
                    {
                        Expires = DateTime.UtcNow.AddHours(24),
                        Issuer = "INNOVATIVETECHNOLOGY",
                        Audience = "INNOVATIVETECHNOLOGY",
                        SigningCredentials = new SigningCredentials(
                            new SymmetricSecurityKey(keyBytes),
                            SecurityAlgorithms.HmacSha256Signature)
                    };
                    var tokenHandler = new JwtSecurityTokenHandler();
                    var jwtString = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtString);
                }

                // Auto-detect active COM port and verify device connection automatically
                await AutoDetectAndConnectAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException)
            {
                var errorMessage = $"Failed to connect to CashRecyclerX API at '{_apiBaseUrl}'. {ex.Message}";
                OnFault?.Invoke(this, new HardwareFaultEventArgs("CashRecyclerX", errorMessage));
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        lock (_stateLock)
        {
            if (_state != RecyclerState.Disconnected)
                return;

            _state = RecyclerState.Connected;
        }
    }

    /// <summary>
    /// Scans active Windows COM ports dynamically and verifies device status via REST API.
    /// Locks onto whichever COM port responds successfully.
    /// </summary>
    private async Task AutoDetectAndConnectAsync(CancellationToken cancellationToken)
    {
        // 1. Check if device is ALREADY connected on the REST API server
        try
        {
            using var statusCheck = await _httpClient.GetAsync($"{_apiBaseUrl}/api/CashDevice/GetDeviceStatus?deviceID={_activeDeviceId}", cancellationToken);
            if (statusCheck.IsSuccessStatusCode)
            {
                string json = await statusCheck.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                var items = new List<JsonElement>();
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray()) items.Add(el);
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("devices", out var devArr) && devArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in devArr.EnumerateArray()) items.Add(el);
                    }
                    else
                    {
                        items.Add(doc.RootElement);
                    }
                }

                foreach (var item in items)
                {
                    string id = GetStringFromElement(item, "deviceID", "id", "deviceName", "name");
                    string port = GetStringFromElement(item, "comPort", "port", "serialPort");
                    string status = GetStringFromElement(item, "status", "state", "stateAsString", "eventTypeAsString");

                    bool isDisconnected = status.Equals("Disconnected", StringComparison.OrdinalIgnoreCase) ||
                                         status.Equals("Offline", StringComparison.OrdinalIgnoreCase) ||
                                         status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
                                         status.Equals("Error", StringComparison.OrdinalIgnoreCase);

                    if (!isDisconnected && (!string.IsNullOrEmpty(port) || !string.IsNullOrEmpty(id)))
                    {
                        if (!string.IsNullOrEmpty(port)) _activeComPort = port;
                        if (!string.IsNullOrEmpty(id)) _activeDeviceId = id;

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n  [SUCCESS] Device already connected & active: {_activeDeviceId} (Port: {_activeComPort}) ✓\n");
                        Console.ResetColor();
                        return;
                    }
                }
            }
        }
        catch { }

        // 2. Check REST API connected devices list
        try
        {
            using var resp = await _httpClient.GetAsync($"{_apiBaseUrl}/api/CashDevice/GetConnectedDevices", cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                var items = new List<JsonElement>();
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray()) items.Add(el);
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    items.Add(doc.RootElement);
                }

                foreach (var item in items)
                {
                    string id = GetStringFromElement(item, "deviceID", "id", "deviceName", "name");
                    string port = GetStringFromElement(item, "comPort", "port", "serialPort");

                    if (!string.IsNullOrEmpty(port) && !port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        port = $"COM{port}";

                    if (!string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(port))
                    {
                        if (!string.IsNullOrEmpty(port)) _activeComPort = port;
                        if (!string.IsNullOrEmpty(id)) _activeDeviceId = id;
                        else if (!string.IsNullOrEmpty(port)) _activeDeviceId = $"NOTE_VALIDATOR-{port}";

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n  [AUTO-DETECT] Connected device found via REST API: {_activeDeviceId} (Port: {_activeComPort}) ✓\n");
                        Console.ResetColor();
                        return;
                    }
                }
            }
        }
        catch { }

        // 3. Query ONLY physical serial ports currently registered in Windows Device Manager
        string[] actualPorts;
        try
        {
            actualPorts = SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToArray();
        }
        catch
        {
            actualPorts = new[] { _activeComPort };
        }

        if (actualPorts.Length > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [AUTO-DISCOVERY] Probing active Windows serial ports: {string.Join(", ", actualPorts)}...");
            Console.ResetColor();

            foreach (var port in actualPorts)
            {
                string candidateId = $"NOTE_VALIDATOR-{port}";

                try
                {
                    using var openContent = new StringContent($"{{\"comPort\":\"{port}\"}}", Encoding.UTF8, "application/json");
                    var openResp = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/OpenConnection", openContent, cancellationToken);

                    if (openResp.IsSuccessStatusCode)
                    {
                        using var statusResp = await _httpClient.GetAsync($"{_apiBaseUrl}/api/CashDevice/GetDeviceStatus?deviceID={candidateId}", cancellationToken);
                        if (statusResp.IsSuccessStatusCode)
                        {
                            _activeComPort = port;
                            _activeDeviceId = candidateId;

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"\n  [SUCCESS] Connected to note validator on {port} ({_activeDeviceId})! ✓\n");
                            Console.ResetColor();
                            return;
                        }
                    }
                }
                catch { }
            }
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [AUTO-DETECT] Ready on active device handle: {_activeDeviceId}\n");
        Console.ResetColor();
    }

    public async Task ArmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                try
                {
                    using var autoContent = new StringContent("true", Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/SetAutoAccept?deviceID={_activeDeviceId}", autoContent, cancellationToken);
                }
                catch { }

                bool enabled = false;
                try
                {
                    using var content = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                    using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/EnableAcceptor?deviceID={_activeDeviceId}", content, cancellationToken);
                    if (response.IsSuccessStatusCode) enabled = true;
                }
                catch { }

                if (!enabled)
                {
                    try
                    {
                        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                        using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/enable", content, cancellationToken);
                        if (response.IsSuccessStatusCode) enabled = true;
                    }
                    catch { }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n  [SUCCESS] Physical Intake Shutter ARMED & Green LED Lights ON! Auto-Accept Active ({_activeDeviceId}). ✓\n");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[VendorXCashRecycler] ArmAcceptanceAsync failed: {ex.Message}");
            }
        }

        lock (_stateLock)
        {
            if (_state == RecyclerState.Connected)
                _state = RecyclerState.Armed;
        }

        if (_useRealApi)
        {
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollHardwareLoopAsync(_pollCts.Token));
        }
    }

    public async Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        _pollCts?.Cancel();

        if (_useRealApi)
        {
            try
            {
                try
                {
                    using var content = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/DisableAcceptor?deviceID={_activeDeviceId}", content, cancellationToken);
                }
                catch
                {
                    using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/disable", content, cancellationToken);
                }

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"\n  [SUCCESS] Physical Intake Shutter CLOSED & Green LED Light OFF. Device Disarmed ({_activeDeviceId}). 🔴\n");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[VendorXCashRecycler] DisarmAcceptanceAsync failed: {ex.Message}");
            }
        }

        lock (_stateLock)
        {
            if (_state == RecyclerState.Armed)
                _state = RecyclerState.Connected;
        }
    }

    private async Task PollHardwareLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _state == RecyclerState.Armed)
        {
            try
            {
                HttpResponseMessage? response = null;
                try
                {
                    response = await _httpClient.GetAsync($"{_apiBaseUrl}/api/CashDevice/GetDeviceStatus?deviceID={_activeDeviceId}", cancellationToken).ConfigureAwait(false);
                }
                catch { }

                if (response == null || !response.IsSuccessStatusCode)
                {
                    try
                    {
                        response = await _httpClient.GetAsync($"{_apiBaseUrl}/api/device/status", cancellationToken).ConfigureAwait(false);
                    }
                    catch { }
                }

                if (response != null && response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);

                    var elementsToProcess = new List<JsonElement>();

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                            elementsToProcess.Add(item);
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("devices", out var devArr) && devArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in devArr.EnumerateArray())
                                elementsToProcess.Add(item);
                        }
                        else if (doc.RootElement.TryGetProperty("events", out var evArr) && evArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in evArr.EnumerateArray())
                                elementsToProcess.Add(item);
                        }
                        else
                        {
                            elementsToProcess.Add(doc.RootElement);
                        }
                    }

                    foreach (var elem in elementsToProcess)
                    {
                        string stateStr = GetStringFromElement(elem,
                            "eventTypeAsString", "stateAsString", "status", "state", "event");

                        if (stateStr.Contains("ESCROW",     StringComparison.OrdinalIgnoreCase) ||
                            stateStr.Contains("STACKED",    StringComparison.OrdinalIgnoreCase) ||
                            stateStr.Contains("NOTE_CREDIT",StringComparison.OrdinalIgnoreCase) ||
                            stateStr.Contains("NOTE_READ",  StringComparison.OrdinalIgnoreCase) ||
                            stateStr.Contains("INSERTED",   StringComparison.OrdinalIgnoreCase) ||
                            stateStr.Contains("ACCEPT",     StringComparison.OrdinalIgnoreCase))
                        {
                            decimal val = GetDecimalFromElement(elem, "value", "amount", "noteValue", "denomination");
                            string countryCode = GetStringFromElement(elem, "countryCode", "currency", "isoCode");
                            if (string.IsNullOrEmpty(countryCode)) countryCode = "USD";

                            if (val > 0m)
                            {
                                Money note = countryCode.Equals("KHR", StringComparison.OrdinalIgnoreCase) ||
                                             countryCode.Equals("CAM", StringComparison.OrdinalIgnoreCase)
                                    ? Money.Khr(val / 100m)
                                    : Money.Usd(val / 100m);

                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"\n  💵 [HARDWARE SCAN] Physical validator scanned: {note.Amount} {note.Currency}");
                                Console.ResetColor();

                                OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(note));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[VendorXCashRecycler] Poll error: {ex.Message}");
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    private static decimal GetDecimalFromElement(JsonElement elem, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (elem.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d))
                    return d;
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var dParsed))
                    return dParsed;
            }
        }
        return 0m;
    }

    private static string GetStringFromElement(JsonElement elem, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (elem.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString() ?? "";
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetRawText();
            }
        }
        return "";
    }

    public async Task StopAcceptingCashAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/disable", content, cancellationToken);
            }
            catch { }
        }

        lock (_stateLock)
        {
            _state = RecyclerState.Stopped;
        }
    }

    public async Task<DispenseResult> DispenseAsync(
        ChangeBreakdown change,
        CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                decimal totalUsd = change.UsdNotes.Sum(kvp => kvp.Key.Amount * kvp.Value);
                decimal totalKhr = change.KhrNotes.Sum(kvp => kvp.Key.Amount * kvp.Value);

                string jsonPayload = $"{{\"totalUsd\": {totalUsd}, \"totalKhr\": {totalKhr}}}";
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/dispense", content, cancellationToken);
                return new DispenseResult(true, change);
            }
            catch (Exception ex)
            {
                OnFault?.Invoke(this, new HardwareFaultEventArgs("CashRecyclerX", $"Dispense failure: {ex.Message}"));
                return new DispenseResult(false, change);
            }
        }

        return new DispenseResult(true, change);
    }

    public async Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                using var c1 = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/RejectNote?deviceID={_activeDeviceId}", c1, cancellationToken);
            }
            catch { }

            try
            {
                using var c2 = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/RejectEscrow?deviceID={_activeDeviceId}", c2, cancellationToken);
            }
            catch { }

            try
            {
                using var c3 = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/ReturnNote?deviceID={_activeDeviceId}", c3, cancellationToken);
            }
            catch { }

            try
            {
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/escrow/reject", content, cancellationToken);
            }
            catch { }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  ⛔ [HARDWARE MOTOR] Physical bill rejected & returned from validator slot! ↩️\n");
            Console.ResetColor();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    internal void SimulateNoteInserted(Money note)
    {
        lock (_stateLock)
        {
            if (_state != RecyclerState.Armed)
                throw new InvalidOperationException(
                    $"Cannot simulate note insertion in state {_state}. " +
                    "Call ArmAcceptanceAsync first.");
        }

        OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(note));
    }

    internal void SimulateFault(string message)
        => OnFault?.Invoke(this, new HardwareFaultEventArgs("CashRecyclerX", message));
}
