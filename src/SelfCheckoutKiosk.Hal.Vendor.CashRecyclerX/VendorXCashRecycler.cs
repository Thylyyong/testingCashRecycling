using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;

// Grant the integration-test project access to internal simulation helpers.
// Production code never calls these — only test fakes do.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfCheckoutKiosk.Integration.Tests")]

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Simulation adapter for the "CashRecyclerX" SKU (Blueprint §4).
/// Implements <see cref="ICashRecycler"/> against a software-only device state
/// machine — no real vendor SDK is required for Sprint 0 / unit testing.
///
/// ARCHITECTURE RULES — do not violate:
///   1. This adapter depends on SelfCheckoutKiosk.Core ONLY. No reference to
///      Infrastructure or App is ever allowed here.
///   2. Zero business logic lives in this class. It models the physical device
///      state (Disconnected / Connected / Armed / Stopped) and translates
///      commands to state transitions or event fires — nothing more.
///   3. The engine (LLCoreLogicEngine) is the sole subscriber to the events
///      raised here. No other class may subscribe directly.
///   4. When the real vendor SDK is available (Sprint 1+), replace the
///      simulation state machine below with real SDK calls — the ICashRecycler
///      surface remains unchanged, so Core and tests need no modification.
///
/// TODO(Systems/Sprint 1): wire the real vendor SDK once the package is available.
/// Retarget .csproj to net10.0-windows if the SDK requires it (coordinate with Lead).
/// </summary>
public sealed class VendorXCashRecycler : ICashRecycler
{
    // -----------------------------------------------------------------------
    // Simulation state machine
    // -----------------------------------------------------------------------
    private enum RecyclerState { Disconnected, Connected, Armed, Stopped }

    private readonly object _stateLock = new();
    private RecyclerState _state = RecyclerState.Disconnected;

    // -----------------------------------------------------------------------
    // ICashRecycler events
    // -----------------------------------------------------------------------
    public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
    public event EventHandler<HardwareFaultEventArgs>? OnFault;

    // -----------------------------------------------------------------------
    // ICashRecycler commands
    // -----------------------------------------------------------------------

    /// <summary>
    /// Simulates opening a connection to the cash recycler hardware.
    /// In production this would open the vendor SDK session / serial port.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state != RecyclerState.Disconnected)
                return Task.CompletedTask; // idempotent

            _state = RecyclerState.Connected;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulates arming the recycler to accept incoming notes.
    /// Device must be Connected first.
    /// </summary>
    public Task ArmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state == RecyclerState.Connected)
                _state = RecyclerState.Armed;
            // Silently ignore if already Armed or in other states (idempotent simulation).
        }

        return Task.CompletedTask;
    }

    /// <summary>Simulates a normal disarm — device stops accepting notes, returns to Connected.</summary>
    public Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state == RecyclerState.Armed)
                _state = RecyclerState.Connected;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Hard stop on cash intake — driven by the low-float safeguard (Blueprint §4).
    /// Called by the engine immediately on <see cref="Core.Currency.LowFloatMonitor.LowFloatStateTriggered"/>.
    /// Transitions to Stopped from any state.
    /// </summary>
    public Task StopAcceptingCashAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            _state = RecyclerState.Stopped;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulates dispensing change. Returns success with the exact <paramref name="change"/>
    /// breakdown requested (simulation always succeeds). Real SDK would command the
    /// cassette motors and confirm physical note ejection.
    /// </summary>
    public Task<DispenseResult> DispenseAsync(
        ChangeBreakdown change,
        CancellationToken cancellationToken = default)
    {
        // Simulation: report success with the requested breakdown.
        // Real SDK: command the cassette and confirm physical ejection.
        return Task.FromResult(new DispenseResult(true, change));
    }

    /// <summary>
    /// Simulates returning the escrowed note to the customer.
    /// No state change required — device remains Armed after a reject.
    /// </summary>
    public Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default)
    {
        // Simulation: immediate return. Real SDK: command the reject gate.
        return Task.CompletedTask;
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

