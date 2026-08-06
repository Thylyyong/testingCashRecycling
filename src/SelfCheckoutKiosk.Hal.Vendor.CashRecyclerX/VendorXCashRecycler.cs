using System.Net.Http;
using System.Text;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;

// Grant the integration-test project access to internal simulation helpers.
// Production code never calls these — only test fakes do.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfCheckoutKiosk.Integration.Tests")]

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
    private readonly bool _useRealApi;

    /// <summary>
    /// Initializes a new instance of <see cref="VendorXCashRecycler"/>.
    /// </summary>
    /// <param name="apiBaseUrl">Base address of the running vendor REST server (default: http://localhost:5000).</param>
    /// <param name="useRealApi">If set to <c>true</c>, actual HTTP commands are dispatched to the machine; otherwise runs in test simulation mode.</param>
    /// <param name="httpClient">Optional custom <see cref="HttpClient"/> instance.</param>
    public VendorXCashRecycler(string apiBaseUrl = "http://localhost:5000", bool useRealApi = false, HttpClient? httpClient = null)
    {
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _useRealApi = useRealApi;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
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
                using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/connect", null, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                OnFault?.Invoke(this, new HardwareFaultEventArgs("CashRecyclerX", $"REST API Connect error: {ex.Message}"));
                throw;
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
            using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/enable", null, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        lock (_stateLock)
        {
            if (_state == RecyclerState.Connected)
                _state = RecyclerState.Armed;
        }
    }

    /// <summary>Normal disarm — device stops accepting notes and closes intake slot via REST API.</summary>
    public async Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        if (_useRealApi)
        {
            using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/disable", null, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        lock (_stateLock)
        {
            if (_state == RecyclerState.Armed)
                _state = RecyclerState.Connected;
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
                await _httpClient.PostAsync($"{_apiBaseUrl}/api/device/disable", null, cancellationToken);
            }
            catch
            {
                // Best-effort attempt during hard safeguard stop
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
                // Construct simple JSON payload without reflection for Native AOT safety
                string jsonPayload = $"{{\"totalUsd\": {change.TotalUsd.Amount}, \"totalKhr\": {change.TotalKhr.Amount}}}";
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
            using var response = await _httpClient.PostAsync($"{_apiBaseUrl}/api/escrow/reject", null, cancellationToken);
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

