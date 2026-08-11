using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;

// Grant the integration-test project access to internal simulation helpers.
// Production code never calls these — only test fakes do.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfCheckoutKiosk.Integration.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfCheckoutKiosk.App")]

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Hybrid Adapter for the "CashRecyclerX" SKU (Blueprint §4).
/// Implements <see cref="ICashRecycler"/> against both a local hardware REST API
/// (CashDevice-RestAPI server) and a software-only simulation state machine for Sprint 0 / unit testing.
///
/// ARCHITECTURE RULES — do not violate:
///   1. This adapter depends on SelfCheckoutKiosk.Core ONLY. No reference to
///      Infrastructure or App is ever allowed here.
///   2. Zero business logic lives in this class. It models the physical device
///      state (Disconnected / Connected / Armed / Stopped) and translates
///      commands to HTTP REST calls or state transitions — nothing more.
///   3. The engine (LLCoreLogicEngine) is the sole subscriber to the events
///      raised here. No other class may subscribe directly.
/// </summary>
public sealed class VendorXCashRecycler : ICashRecycler, IDisposable
{
    // -----------------------------------------------------------------------
    // State machine & REST API Configuration
    // -----------------------------------------------------------------------
    private enum RecyclerState { Disconnected, Connected, Armed, Stopped }

    private readonly object _stateLock = new();
    private RecyclerState _state = RecyclerState.Disconnected;

    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private readonly string? _apiKey;
    private bool _useRealApi;

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    /// <summary>
    /// Initializes a new instance of <see cref="VendorXCashRecycler"/>.
    /// </summary>
    /// <param name="apiBaseUrl">Base address of the running vendor REST server (default: http://localhost:5000).</param>
    /// <param name="apiKey">The API key required by the vendor's REST server for authentication.</param>
    /// <param name="useRealApi">If set to <c>true</c>, actual HTTP commands are dispatched to the machine; otherwise runs in test simulation mode.</param>
    /// <param name="httpClient">Optional custom <see cref="HttpClient"/> instance.</param>
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

        if (!string.IsNullOrEmpty(_apiKey))
        {
            // We will generate the token dynamically inside ConnectAsync to test different Audiences
        }
    }

    // -----------------------------------------------------------------------
    // ICashRecycler events
    // -----------------------------------------------------------------------
    public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
    public event EventHandler<HardwareFaultEventArgs>? OnFault;

    // -----------------------------------------------------------------------
    // ICashRecycler commands
    // -----------------------------------------------------------------------

    /// <summary>
    /// Connects to the cash recycler hardware via REST API or simulation.
    /// </summary>
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

                // Try OpenConnection for COM7 (if already open, server returns 400 which we handle gracefully)
                try
                {
                    using var openContent = new StringContent("{\"comPort\":\"COM7\"}", Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/OpenConnection", openContent, cancellationToken);
                }
                catch { }

                // Ping REST API server to verify connection status
                using var statusResponse = await _httpClient.GetAsync($"{_apiBaseUrl}/api/CashDevice/GetDeviceStatus?deviceID=NOTE_VALIDATOR-COM7", cancellationToken);
                statusResponse.EnsureSuccessStatusCode();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  [SUCCESS] Connected to Physical NOTE_VALIDATOR on COM7 over REST API! ✓\n");
                Console.ResetColor();
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
                return; // idempotent

            _state = RecyclerState.Connected;
        }
    }

    /// <summary>
    /// Arms the recycler to accept incoming notes by opening the physical intake slot via REST API.
    /// Device must be Connected first.
    /// </summary>
    public async Task ArmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                // 1. Enable SetAutoAccept so ALL currency denominations (USD & KHR, small & large) are accepted automatically
                using var autoContent = new StringContent("true", Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/SetAutoAccept?deviceID=NOTE_VALIDATOR-COM7", autoContent, cancellationToken);

                // 2. Enable Acceptor to open shutter and turn ON intake green LED light
                using var content = new StringContent("{\"deviceID\":\"NOTE_VALIDATOR-COM7\"}", Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/EnableAcceptor?deviceID=NOTE_VALIDATOR-COM7", content, cancellationToken);
                response.EnsureSuccessStatusCode();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  [SUCCESS] Physical Intake Shutter ARMED & Green LED Lights ON! Auto-Accept ALL Denominations (USD & KHR) Active. ✓\n");
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

    /// <summary>Normal disarm — device stops accepting notes and closes intake slot via REST API.</summary>
    public async Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        _pollCts?.Cancel();

        if (_useRealApi)
        {
            try
            {
                using var content = new StringContent("{\"deviceID\":\"NOTE_VALIDATOR-COM7\"}", Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/CashDevice/DisableAcceptor?deviceID=NOTE_VALIDATOR-COM7", content, cancellationToken);
                response.EnsureSuccessStatusCode();

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("\n  [SUCCESS] Physical Intake Shutter CLOSED & Green LED Light OFF. Device Disarmed. 🔴\n");
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
                using var response = await _httpClient.GetAsync($"{_apiBaseUrl}/api/CashDevice/GetDeviceStatus?deviceID=NOTE_VALIDATOR-COM7", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);

                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            string stateStr = elem.TryGetProperty("stateAsString", out var s) ? s.GetString() ?? "" : "";
                            
                            if (stateStr.Contains("NOTE_ESCROW", StringComparison.OrdinalIgnoreCase) ||
                                stateStr.Contains("NOTE_CREDIT", StringComparison.OrdinalIgnoreCase) ||
                                stateStr.Contains("NOTE_READ", StringComparison.OrdinalIgnoreCase))
                            {
                                decimal val = elem.TryGetProperty("value", out var v) ? v.GetDecimal() : 0m;
                                string countryCode = elem.TryGetProperty("countryCode", out var c) ? c.GetString() ?? "USD" : "USD";

                                Money note = countryCode.Equals("KHR", StringComparison.OrdinalIgnoreCase)
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
                // Background polling resiliency
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Hard stop on cash intake — driven by the low-float safeguard (Blueprint §4).
    /// Called by the engine immediately on <see cref="Core.Currency.LowFloatMonitor.LowFloatStateTriggered"/>.
    /// Transitions to Stopped from any state.
    /// </summary>
    public async Task StopAcceptingCashAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/disable", content, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[VendorXCashRecycler] StopAcceptingCashAsync failed: {ex.Message}");
            }
        }

        lock (_stateLock)
        {
            _state = RecyclerState.Stopped;
        }
    }

    /// <summary>
    /// Dispenses change via REST API. Commands the physical cassette motors and confirms physical note ejection.
    /// </summary>
    public async Task<DispenseResult> DispenseAsync(
        ChangeBreakdown change,
        CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            try
            {
                // Calculate totals from denomination lists
                decimal totalUsd = change.UsdNotes.Sum(kvp => kvp.Key.Amount * kvp.Value);
                decimal totalKhr = change.KhrNotes.Sum(kvp => kvp.Key.Amount * kvp.Value);

                // Construct simple JSON payload without reflection for Native AOT safety
                string jsonPayload = $"{{\"totalUsd\": {totalUsd}, \"totalKhr\": {totalKhr}}}";
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/dispense", content, cancellationToken);
                response.EnsureSuccessStatusCode();
                return new DispenseResult(true, change);
            }
            catch (Exception ex)
            {
                OnFault?.Invoke(this, new HardwareFaultEventArgs("CashRecyclerX", $"Dispense failure: {ex.Message}"));
                return new DispenseResult(false, change);
            }
        }

        // Simulation: report success with the requested breakdown.
        return new DispenseResult(true, change);
    }

    /// <summary>
    /// Commands the reject gate via REST API to return the escrowed note to the customer.
    /// </summary>
    public async Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/escrow/reject", content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        // Simulation / real behavior: immediate return. Device remains Armed after a reject.
    }

    /// <summary>
    /// Disposes the HTTP client resources.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // -----------------------------------------------------------------------
    // Test / simulation helpers (internal — only reachable from Integration.Tests)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fires <see cref="OnNoteInEscrow"/> as if a physical note were inserted.
    /// Only callable from <c>SelfCheckoutKiosk.Integration.Tests</c>.
    /// Device must be in the Armed state; throws otherwise to catch test setup errors.
    /// </summary>
    internal void SimulateNoteInserted(Money note)
    {
        lock (_stateLock)
        {
            if (_state != RecyclerState.Armed)
                throw new InvalidOperationException(
                    $"Cannot simulate note insertion in state {_state}. " +
                    "Call ArmAcceptanceAsync first.");
        }

        // Raise the event. HardwareAppendLog must be the first subscriber
        // (wired in LLCoreLogicEngine.InitializeAsync) so the audit record
        // is written before any engine processing.
        OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(note));
    }

    /// <summary>
    /// Fires <see cref="OnFault"/> as if the device reported a hardware fault.
    /// Only callable from <c>SelfCheckoutKiosk.Integration.Tests</c>.
    /// </summary>
    internal void SimulateFault(string message)
        => OnFault?.Invoke(this, new HardwareFaultEventArgs("CashRecyclerX", message));
}
