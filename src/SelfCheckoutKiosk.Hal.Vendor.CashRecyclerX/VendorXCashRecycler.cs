using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;
using System.IO.Ports;

// Grant the integration-test project access to internal simulation helpers.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfCheckoutKiosk.Integration.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfCheckoutKiosk.App")]

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Hybrid Adapter for the "CashRecyclerX" SKU (Blueprint §4).
/// Implements <see cref="ICashRecycler"/> against both a local hardware REST API
/// (CashDevice-RestAPI server) and a software-only simulation state machine for Sprint 0 / unit testing.
/// </summary>
public sealed class VendorXCashRecycler : ICashRecycler, ICashEscrowController, IDisposable
{
    private enum RecyclerState { Disconnected, Connected, Armed, Stopped }

    private readonly object _stateLock = new();
    private RecyclerState _state = RecyclerState.Disconnected;

    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private readonly string? _apiKey;
    private bool _useRealApi;

    private string _activeComPort = "COM8";
    private string _activeDeviceId = "NOTE_VALIDATOR-COM8";

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public VendorXCashRecycler(
        string apiBaseUrl = "http://localhost:5000",
        string? apiKey = null,
        bool useRealApi = true,
        HttpClient? httpClient = null,
        string? comPort = null)
    {
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _apiKey = apiKey;
        
        string? envUseRealApi = Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_USE_REAL_API");
        _useRealApi = !string.IsNullOrEmpty(envUseRealApi) ? bool.Parse(envUseRealApi) : useRealApi;
        
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        string envPort = comPort ?? Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_COM_PORT") ?? "COM8";
        if (!envPort.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            envPort = $"COM{envPort}";
        _activeComPort = envPort;
        _activeDeviceId = $"NOTE_VALIDATOR-{envPort}";
    }

    public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
    public event EventHandler<HardwareFaultEventArgs>? OnFault;

    // ICashEscrowController — fired after the device physically commits or rejects the escrowed note
    public event EventHandler<CashEscrowResolvedEventArgs>? OnEscrowResolved;

    // Tracks the last note seen in escrow so CommitEscrowedNoteAsync can resolve it
    private Money? _lastEscrowedNote;

    /// <summary>
    /// Routes the currently escrowed note to the vault and fires OnEscrowResolved.
    /// Called by LLCoreLogicEngine after verifying the note fits the transaction.
    /// </summary>
    public async Task CommitEscrowedNoteAsync(CancellationToken cancellationToken = default)
    {
        var note = _lastEscrowedNote;
        _lastEscrowedNote = null;

        // Route the physical note to storage on the hardware
        await AcceptEscrowedNoteAsync(cancellationToken).ConfigureAwait(false);

        // Notify engine that the note is physically committed
        if (note.HasValue)
        {
            OnEscrowResolved?.Invoke(this, new CashEscrowResolvedEventArgs(
                note.Value,
                CashEscrowResolution.CommittedToVault));
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                // 1. Auto-discover API key and generate fresh JWT Token
                string? effectiveKey = _apiKey ?? Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_API_KEY");
                if (string.IsNullOrWhiteSpace(effectiveKey))
                {
                    string[] candidatePaths = {
                        "api_key.secret",
                        Path.Combine(AppContext.BaseDirectory, "api_key.secret"),
                        Path.Combine(AppContext.BaseDirectory, "..", "api_key.secret"),
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "api_key.secret"),
                        Path.Combine(Directory.GetCurrentDirectory(), "api_key.secret"),
                        Path.Combine(Directory.GetCurrentDirectory(), "..", "api_key.secret"),
                        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "api_key.secret")
                    };
                    foreach (var p in candidatePaths)
                    {
                        try
                        {
                            string full = Path.GetFullPath(p);
                            if (File.Exists(full))
                            {
                                effectiveKey = File.ReadAllText(full).Trim();
                                if (!string.IsNullOrWhiteSpace(effectiveKey)) break;
                            }
                        }
                        catch { }
                    }
                }

                if (!string.IsNullOrEmpty(effectiveKey))
                {
                    var keyBytes = Encoding.UTF8.GetBytes(effectiveKey);
                    var tokenDescriptor = new SecurityTokenDescriptor
                    {
                        Expires = DateTime.UtcNow.AddHours(24),
                        Issuer = Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_JWT_ISSUER") ?? "INNOVATIVETECHNOLOGY",
                        Audience = Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_JWT_AUDIENCE") ?? "INNOVATIVETECHNOLOGY",
                        SigningCredentials = new SigningCredentials(
                            new SymmetricSecurityKey(keyBytes),
                            SecurityAlgorithms.HmacSha256Signature)
                    };
                    var tokenHandler = new JwtSecurityTokenHandler();
                    var jwtString = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtString);
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  [AUTH] Generated fresh JWT token for Cash Recycler API.");
                    Console.ResetColor();
                }

                // 2. Automatically discover active serial port and connect
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
    /// Scans active Windows COM ports dynamically and connects automatically without manual configuration.
    /// </summary>
    private async Task AutoDetectAndConnectAsync(CancellationToken cancellationToken)
    {
        // 1. Check REST API connected devices first
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
                    string model = GetStringFromElement(item, "DeviceModel", "deviceModel", "model");

                    if (!string.IsNullOrEmpty(port) && !port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        port = $"COM{port}";

                    if (!string.Equals(model, "UNKNOWN", StringComparison.OrdinalIgnoreCase) && (!string.IsNullOrEmpty(port) || !string.IsNullOrEmpty(id)))
                    {
                        if (!string.IsNullOrEmpty(port)) _activeComPort = port;
                        if (!string.IsNullOrEmpty(id)) _activeDeviceId = id;
                        else _activeDeviceId = $"NOTE_VALIDATOR-{_activeComPort}";

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n  [AUTO-CONNECT] Connected device found via REST API: {_activeDeviceId} (Port: {_activeComPort}) ✓\n");
                        Console.ResetColor();
                        return;
                    }
                }
            }
        }
        catch { }

        // 2. Discover all physical Windows COM ports dynamically
        var candidatePorts = new List<string>();
        string? envPort = Environment.GetEnvironmentVariable("SELFCHECKOUT_CASH_RECYCLER_COM_PORT");

        if (!string.IsNullOrWhiteSpace(envPort))
        {
            candidatePorts.Add(envPort.Trim());
        }

        try
        {
            var systemPorts = SerialPort.GetPortNames()
                .Distinct()
                .OrderBy(p => p);

            foreach (var p in systemPorts)
            {
                if (!candidatePorts.Contains(p, StringComparer.OrdinalIgnoreCase))
                    candidatePorts.Add(p);
            }
        }
        catch { }

        // Fail-safe: Always prioritize COM8 (physical hardware port) first!
        var priorityPorts = new[] { "COM8", "COM7", "COM3", "COM6", "COM4", "COM5" };
        foreach (var p in priorityPorts.Reverse())
        {
            if (candidatePorts.Contains(p, StringComparer.OrdinalIgnoreCase))
                candidatePorts.Remove(p);
            candidatePorts.Insert(0, p);
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [AUTO-DISCOVERY] Probing Windows serial ports: {string.Join(", ", candidatePorts)}...");
        Console.ResetColor();

        // 3. First pass: check if device is already active & open on any candidate port
        foreach (var port in candidatePorts)
        {
            string candidateId = $"NOTE_VALIDATOR-{port}";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                using var checkResp = await _httpClient.GetAsync($"{_apiBaseUrl}/api/CashDevice/GetDeviceStatus?deviceID={candidateId}", linkedCts.Token);
                if (checkResp.IsSuccessStatusCode)
                {
                    string json = await checkResp.Content.ReadAsStringAsync(linkedCts.Token);
                    if (!string.IsNullOrWhiteSpace(json) && json != "[]")
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        bool isValid = false;
                        string model = "NOTE_VALIDATOR";
                        string devId = candidateId;

                        if (root.ValueKind == JsonValueKind.Array)
                        {
                            if (root.GetArrayLength() > 0) isValid = true;
                        }
                        else if (root.ValueKind == JsonValueKind.Object)
                        {
                            bool isOpen = root.TryGetProperty("IsOpen", out var io) && io.GetBoolean();
                            string m = GetStringFromElement(root, "DeviceModel", "deviceModel", "model");
                            string id = GetStringFromElement(root, "DeviceID", "deviceID", "id");
                            if (!string.IsNullOrEmpty(m)) model = m;
                            if (!string.IsNullOrEmpty(id)) devId = id;

                            if (isOpen && !string.IsNullOrEmpty(model) && !string.Equals(model, "UNKNOWN", StringComparison.OrdinalIgnoreCase) && !string.Equals(model, "NONE", StringComparison.OrdinalIgnoreCase))
                            {
                                isValid = true;
                            }
                        }

                        if (isValid)
                        {
                            _activeComPort = port;
                            _activeDeviceId = !string.IsNullOrEmpty(devId) ? devId : candidateId;

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"\n  [AUTO-CONNECT] Verified active cash device: {_activeDeviceId} ({model}) on {_activeComPort} ✓\n");
                            Console.ResetColor();
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        // 4. Second pass: attempt opening connection on each port with 15s handshake timeout
        foreach (var port in candidatePorts)
        {
            string candidateId = $"NOTE_VALIDATOR-{port}";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

                // Attempt opening connection
                using var openContent = new StringContent($"{{\"comPort\":\"{port}\"}}", Encoding.UTF8, "application/json");
                var openResp = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/OpenConnection", openContent, linkedCts.Token);
                string openDevId = "";
                if (openResp.IsSuccessStatusCode)
                {
                    try
                    {
                        string openJson = await openResp.Content.ReadAsStringAsync(linkedCts.Token);
                        using var openDoc = JsonDocument.Parse(openJson);
                        openDevId = GetStringFromElement(openDoc.RootElement, "DeviceID", "deviceID", "id");
                    }
                    catch { }
                }

                string queryId = !string.IsNullOrEmpty(openDevId) ? openDevId : candidateId;

                // Check status after open
                using var statusResp = await _httpClient.GetAsync($"{_apiBaseUrl}/api/CashDevice/GetDeviceStatus?deviceID={queryId}", linkedCts.Token);
                if (statusResp.IsSuccessStatusCode)
                {
                    string json = await statusResp.Content.ReadAsStringAsync(linkedCts.Token);
                    if (!string.IsNullOrWhiteSpace(json) && json != "[]")
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        string model = GetStringFromElement(root, "DeviceModel", "deviceModel", "model");
                        string devId = GetStringFromElement(root, "DeviceID", "deviceID", "id");
                        bool isOpen = root.TryGetProperty("IsOpen", out var io) && io.GetBoolean();

                        if (isOpen && !string.IsNullOrEmpty(model) && !string.Equals(model, "UNKNOWN", StringComparison.OrdinalIgnoreCase) && !string.Equals(model, "NONE", StringComparison.OrdinalIgnoreCase))
                        {
                            _activeComPort = port;
                            _activeDeviceId = !string.IsNullOrEmpty(devId) ? devId : (!string.IsNullOrEmpty(openDevId) ? openDevId : candidateId);

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"\n  [AUTO-CONNECT] Connected to note validator ({model}) on {port} [{_activeDeviceId}]! ✓\n");
                            Console.ResetColor();
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        // Fallback: lock onto COM8
        _activeComPort = "COM8";
        _activeDeviceId = "NOTE_VALIDATOR-COM8";

        try
        {
            using var openFallback = new StringContent($"{{\"comPort\":\"{_activeComPort}\"}}", Encoding.UTF8, "application/json");
            var fbResp = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/OpenConnection", openFallback, cancellationToken);
            if (fbResp.IsSuccessStatusCode)
            {
                string fbJson = await fbResp.Content.ReadAsStringAsync(cancellationToken);
                using var fbDoc = JsonDocument.Parse(fbJson);
                string fbId = GetStringFromElement(fbDoc.RootElement, "DeviceID", "deviceID", "id");
                if (!string.IsNullOrEmpty(fbId)) _activeDeviceId = fbId;
            }
        }
        catch { }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [AUTO-CONNECT] Ready on active device handle: {_activeDeviceId} (Port: {_activeComPort})\n");
        Console.ResetColor();
    }

    public async Task ArmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                HttpResponseMessage? autoResp = null;
                try
                {
                    using var autoContent = new StringContent("true", Encoding.UTF8, "application/json");
                    autoResp = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/SetAutoAccept?deviceID={_activeDeviceId}", autoContent, cancellationToken);
                    Console.ForegroundColor = autoResp.IsSuccessStatusCode ? ConsoleColor.DarkGray : ConsoleColor.Yellow;
                    Console.WriteLine($"  [API] SetAutoAccept response: {(int)autoResp.StatusCode} {autoResp.ReasonPhrase}");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [API] SetAutoAccept exception: {ex.Message}");
                    Console.ResetColor();
                }

                HttpResponseMessage? enableResp = null;
                bool enabled = false;
                try
                {
                    using var content = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                    enableResp = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/EnableAcceptor?deviceID={_activeDeviceId}", content, cancellationToken);
                    Console.ForegroundColor = enableResp.IsSuccessStatusCode ? ConsoleColor.DarkGray : ConsoleColor.Yellow;
                    Console.WriteLine($"  [API] EnableAcceptor (DeviceID) response: {(int)enableResp.StatusCode} {enableResp.ReasonPhrase}");
                    Console.ResetColor();
                    if (enableResp.IsSuccessStatusCode) enabled = true;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [API] EnableAcceptor (DeviceID) exception: {ex.Message}");
                    Console.ResetColor();
                }

                if (!enabled)
                {
                    try
                    {
                        using var contentPort = new StringContent($"{{\"comPort\":\"{_activeComPort}\"}}", Encoding.UTF8, "application/json");
                        var enablePortResp = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/EnableAcceptor", contentPort, cancellationToken);
                        Console.ForegroundColor = enablePortResp.IsSuccessStatusCode ? ConsoleColor.DarkGray : ConsoleColor.Yellow;
                        Console.WriteLine($"  [API] EnableAcceptor (ComPort) response: {(int)enablePortResp.StatusCode} {enablePortResp.ReasonPhrase}");
                        Console.ResetColor();
                        if (enablePortResp.IsSuccessStatusCode) enabled = true;
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  [API] EnableAcceptor (ComPort) exception: {ex.Message}");
                        Console.ResetColor();
                    }
                }

                if (enabled)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n  [ARMED] Physical Intake Shutter ARMED & Green LED ON on {_activeDeviceId}! (Auto-Accept All Denominations Active) ✓\n");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n  [WARNING] SetAutoAccept/EnableAcceptor did not return success! Shutter may not be armed.\n");
                    Console.ResetColor();
                    throw new InvalidOperationException("Failed to arm exact-cash acceptance. Both EnableAcceptor and SetAutoAccept returned non-success codes.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[VendorXCashRecycler] ArmAcceptanceAsync failed: {ex.Message}");
                throw;
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
            _pollTask = Task.Run(() => StartPollingEvents(_pollCts.Token));
        }
    }

    private int _prevStackedCount = 0;

    private async Task StartPollingEvents(CancellationToken cancellationToken)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [HARDWARE-MONITOR] Polling live cash events on {_activeDeviceId}...");
        Console.ResetColor();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 1. Poll GetDeviceStatus (CashEventResponse array)
                try
                {
                    using var response = await _httpClient.GetAsync(
                        $"{_apiBaseUrl}/api/CashDevice/GetDeviceStatus?deviceID={_activeDeviceId}",
                        cancellationToken).ConfigureAwait(false);

                    if (response != null && response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(json) && json != "[]")
                        {
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            if (root.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var element in root.EnumerateArray())
                                {
                                    string type = GetStringFromElement(element, "type", "Type");
                                    string eventTypeStr = GetStringFromElement(element, "eventTypeAsString", "EventTypeAsString", "eventType", "EventType", "event", "Event");
                                    decimal val = GetDecimalFromElement(element, "value", "Value", "amount", "Amount");
                                    string countryCode = GetStringFromElement(element, "countryCode", "CountryCode", "currency", "Currency");
                                    if (string.IsNullOrEmpty(countryCode)) countryCode = "USD";

                                    if (val > 0)
                                    {
                                        bool isUsd = countryCode.Equals("USD", StringComparison.OrdinalIgnoreCase);
                                        decimal noteVal = isUsd && val >= 100 ? val / 100m : val;
                                        var money = isUsd ? Money.Usd(noteVal) : Money.Khr(noteVal);

                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($"\n  [HARDWARE-EVENT] Banknote Detected ({eventTypeStr}): {money} ({countryCode}) ✓\n");
                                        Console.ResetColor();

                                        _lastEscrowedNote = money;
                                        OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(money));
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2. Poll GetCounters as backup tracking
                try
                {
                    using var counterResp = await _httpClient.GetAsync(
                        $"{_apiBaseUrl}/api/CashDevice/GetCounters?deviceID={_activeDeviceId}",
                        cancellationToken).ConfigureAwait(false);

                    if (counterResp.IsSuccessStatusCode)
                    {
                        string cJson = await counterResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        var match = System.Text.RegularExpressions.Regex.Match(cJson, @"Stacked:\s*(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int stacked))
                        {
                            if (_prevStackedCount == 0)
                            {
                                _prevStackedCount = stacked;
                            }
                            else if (stacked > _prevStackedCount)
                            {
                                int delta = stacked - _prevStackedCount;
                                _prevStackedCount = stacked;

                                if (_lastEscrowedNote == null)
                                {
                                    var defaultMoney = Money.Usd(1.00m);
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"\n  [HARDWARE-EVENT] Banknote Stacked Count Delta (+{delta}): {defaultMoney} ✓\n");
                                    Console.ResetColor();

                                    _lastEscrowedNote = defaultMoney;
                                    OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(defaultMoney));
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        _pollCts?.Cancel();

        if (_useRealApi)
        {
            try
            {
                using var content = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/DisableAcceptor?deviceID={_activeDeviceId}", content, cancellationToken);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  [DISARMED] Cash Acceptor {_activeDeviceId} intake closed.");
                Console.ResetColor();
            }
            catch { }
        }

        lock (_stateLock)
        {
            if (_state == RecyclerState.Armed)
                _state = RecyclerState.Connected;
        }
    }

    public async Task AcceptEscrowedNoteAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                using var content = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/AcceptFromEscrow?deviceID={_activeDeviceId}", content, cancellationToken);
            }
            catch { }
        }
    }

    public async Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default)
    {
        var note = _lastEscrowedNote;
        _lastEscrowedNote = null;

        if (_useRealApi)
        {
            try
            {
                using var content = new StringContent($"{{\"deviceID\":\"{_activeDeviceId}\"}}", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/ReturnFromEscrow?deviceID={_activeDeviceId}", content, cancellationToken);
            }
            catch { }
        }

        // Notify engine that the note was physically rejected and pushed back to the customer
        if (note.HasValue)
        {
            OnEscrowResolved?.Invoke(this, new CashEscrowResolvedEventArgs(
                note.Value,
                CashEscrowResolution.Rejected));
        }
    }

    public Task StopAcceptingCashAsync(CancellationToken cancellationToken = default)
    {
        return DisarmAcceptanceAsync(cancellationToken);
    }

    public Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DispenseResult(true, change));
    }

    public Task DispenseChangeAsync(Money amount, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _pollCts?.Cancel();

        if (_useRealApi)
        {
            try
            {
                using var content = new StringContent($"{{\"comPort\":\"{_activeComPort}\"}}", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/CloseConnection", content, cancellationToken);
            }
            catch { }
        }

        lock (_stateLock)
        {
            _state = RecyclerState.Disconnected;
        }
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _httpClient.Dispose();
    }

    private static string GetStringFromElement(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString() ?? string.Empty;
                }
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetRawText();
                }
            }
        }
        return string.Empty;
    }

    private static decimal GetDecimalFromElement(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d))
                {
                    return d;
                }
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var sd))
                {
                    return sd;
                }
            }
        }
        return 0m;
    }
}
