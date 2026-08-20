using System.IO;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Real ITL CashDevice REST implementation of the Core cash contract.
///
/// Supported physical tender:
///
///     USD
///     KHR
///     mixed USD + KHR
///
/// The ITL event CountryCode identifies the currency of each physical
/// note independently.
///
/// Denomination normalization:
///
///     USD
///         raw 100     -> $1.00
///         raw 500     -> $5.00
///
///     KHR
///         raw 50000   -> 500 KHR
///         raw 100000  -> 1,000 KHR
///         raw 500000  -> 5,000 KHR
///         raw 1000000 -> 10,000 KHR
///
/// The current physical KHR dataset reports denomination values with
/// a 100x scale. The HAL removes that vendor/device representation
/// before exposing Money to Core.
///
/// Core receives normalized Money values and remains vendor-agnostic.
/// </summary>
public sealed class VendorXCashRecycler :
    ICashRecycler,
    ICashEscrowController
{
    private const decimal ItlDenominationScale =
        100m;

    private readonly CashRecyclerXOptions
        _options;

    private readonly CashDeviceRestClient
        _restClient;

    private readonly SemaphoreSlim
        _connectionGate =
            new(
                1,
                1);

    private readonly SemaphoreSlim
        _acceptanceGate =
            new(
                1,
                1);

    private readonly object
        _stateSync =
            new();

    private readonly object
        _escrowSync =
            new();

    private CancellationTokenSource?
        _pollingCancellation;

    private Task?
        _pollingTask;

    private volatile bool
        _isConnected;

    private CashAcceptorState
        _acceptorState =
            CashAcceptorState.Inactive;

    private Money?
        _escrowedNote;

    private EscrowCommandState
        _escrowCommandState;

    /*
     * ============================================================
     * Constructor
     * ============================================================
     */

    public VendorXCashRecycler(
        HttpClient httpClient,
        CashRecyclerXOptions options)
    {
        ArgumentNullException.ThrowIfNull(
            httpClient);

        ArgumentNullException.ThrowIfNull(
            options);

        _options =
            options;

        /*
         * Validate configuration before attempting physical access.
         */

        _ =
            _options
                .ValidateAndCreateBaseUri();

        _restClient =
            new CashDeviceRestClient(
                httpClient,
                options);
    }

    /*
     * ============================================================
     * Events
     * ============================================================
     */

    public event EventHandler<NoteInsertedEventArgs>?
        OnNoteInserted;

    public event EventHandler<NoteInEscrowEventArgs>?
        OnNoteInEscrow;

    public event EventHandler<HardwareFaultEventArgs>?
        OnFault;

    public event EventHandler<CashAcceptorStateChangedEventArgs>?
        OnAcceptorStateChanged;

    public event EventHandler<CashRecyclerJamEventArgs>?
        OnJam;

    public event EventHandler<CashEscrowResolvedEventArgs>?
        OnEscrowResolved;

#pragma warning disable CS0067
    public event EventHandler<CassetteInventoryChangedEventArgs>?
        OnCassetteInventoryChanged;
#pragma warning restore CS0067

    /*
     * ============================================================
     * State
     * ============================================================
     */

    public bool IsConnected =>
        _isConnected;

    public CashAcceptorState AcceptorState
    {
        get
        {
            lock (_stateSync)
            {
                return _acceptorState;
            }
        }
    }

    /*
     * ============================================================
     * Connect
     * ============================================================
     */

    public async Task ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        await _connectionGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_isConnected)
            {
                return;
            }

            await StopPollingAsync()
                .ConfigureAwait(false);

            _restClient
                .ClearSession();

            try
            {
                await _restClient
                    .AuthenticateAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                var connection =
                    await _restClient
                        .OpenConnectionAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                ValidateOpenConnection(
                    connection);

                ResetEscrowTracking();

                _isConnected =
                    true;

                SetAcceptorState(
                    CashAcceptorState.Inactive);

                StartPolling();
            }
            catch (OperationCanceledException)
                when (
                    cancellationToken
                        .IsCancellationRequested)
            {
                _restClient
                    .ClearSession();

                throw;
            }
            catch (Exception exception)
            {
                _isConnected =
                    false;

                _restClient
                    .ClearSession();

                SetAcceptorState(
                    CashAcceptorState.Error);

                RaiseFault(
                    "The ITL cash recycler could not be connected: " +
                    exception.Message);

                throw;
            }
        }
        finally
        {
            _connectionGate
                .Release();
        }
    }

    /*
     * ============================================================
     * Disconnect
     * ============================================================
     */

    public async Task DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        await _connectionGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            Exception?
                disableFailure =
                    null;

            if (
                _restClient
                    .HasOpenDevice)
            {
                // Safely return any banknote remaining in escrow back to the customer on disconnect/shutdown
                try
                {
                    await _restClient
                        .ReturnFromEscrowAsync(
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Best effort return on disconnect/shutdown
                }

                await _acceptanceGate
                    .WaitAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    await _restClient
                        .DisableAcceptorAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                    SetAcceptorState(
                        CashAcceptorState.Inactive);
                }
                catch (Exception exception)
                    when (
                        exception is not
                            OperationCanceledException)
                {
                    disableFailure =
                        exception;

                    SetAcceptorState(
                        CashAcceptorState.Error);
                }
                finally
                {
                    _acceptanceGate
                        .Release();
                }
            }

            lock (_escrowSync)
            {
                if (
                    _escrowedNote is not null)
                {
                    throw new InvalidOperationException(
                        disableFailure is null
                            ? "The ITL cash recycler disabled acceptance but " +
                              "cannot disconnect while a note is awaiting " +
                              "escrow resolution."
                            : "The ITL cash recycler cannot disconnect while " +
                              "a note is awaiting escrow resolution, and " +
                              "acceptance could not be disabled.",
                        disableFailure);
                }
            }

            await StopPollingAsync()
                .ConfigureAwait(false);

            try
            {
                await _restClient
                    .DisconnectDeviceAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _isConnected =
                    false;

                _restClient
                    .ClearSession();

                ResetEscrowTracking();

                SetAcceptorState(
                    CashAcceptorState.Inactive);
            }

            if (
                disableFailure is not null)
            {
                throw new InvalidOperationException(
                    "The ITL device disconnected after acceptance could not " +
                    "be explicitly disabled.",
                    disableFailure);
            }
        }
        finally
        {
            _connectionGate
                .Release();
        }
    }

    /*
     * ============================================================
     * Arm Acceptance
     * ============================================================
     */

    public async Task ArmAcceptanceAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        await _acceptanceGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            EnsureConnected();

            SetAcceptorState(
                CashAcceptorState.Activating);

            try
            {
                /*
                 * Manual escrow is intentional.
                 *
                 * ITL holds the note until the host explicitly calls
                 * AcceptFromEscrow or ReturnFromEscrow.
                 */

                await _restClient
                    .SetAutoAcceptAsync(
                        false,
                        cancellationToken)
                    .ConfigureAwait(false);

                await _restClient
                    .EnableAcceptorAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                EnsureConnected();

                SetAcceptorState(
                    CashAcceptorState.Ready);
            }
            catch
            {
                /*
                 * A timeout or transport failure is ambiguous because the
                 * physical request may still have reached ITL.
                 *
                 * Make one bounded fail-safe disable attempt before exposing
                 * an error state.
                 */

                try
                {
                    await _restClient
                        .DisableAcceptorAsync(
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception disableException)
                {
                    _ =
                        disableException;
                }

                SetAcceptorState(
                    CashAcceptorState.Error);

                throw;
            }
        }
        finally
        {
            _acceptanceGate
                .Release();
        }
    }

    /*
     * ============================================================
     * Disarm Acceptance
     * ============================================================
     */

    public Task DisarmAcceptanceAsync(
        CancellationToken cancellationToken = default)
    {
        return DisableAcceptanceAsync(
            cancellationToken);
    }

    public Task StopAcceptingCashAsync(
        CancellationToken cancellationToken = default)
    {
        return DisableAcceptanceAsync(
            cancellationToken);
    }

    /*
     * ============================================================
     * Dispense
     * ============================================================
     */

    public Task<DispenseResult> DispenseAsync(
        ChangeBreakdown change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            change);

        cancellationToken
            .ThrowIfCancellationRequested();

        EnsureConnected();

        /*
         * Keep payout fail-closed until actual NV200 payout routing and
         * partial-payout behavior are validated.
         */

        throw new NotSupportedException(
            "Physical ITL payout is disabled until value, routing, " +
            "completion, and partial-payout behavior are verified.");
    }

    /*
     * ============================================================
     * Escrow Commit
     * ============================================================
     */

    public async Task CommitEscrowedNoteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        EnsureConnected();

        BeginEscrowCommand(
            EscrowCommandState.CommitRequested);

        await _restClient
            .AcceptFromEscrowAsync(
                cancellationToken)
            .ConfigureAwait(false);
    }

    /*
     * ============================================================
     * Escrow Reject
     * ============================================================
     */

    public async Task RejectEscrowedNoteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        EnsureConnected();

        BeginEscrowCommand(
            EscrowCommandState.ReturnRequested);

        await _restClient
            .ReturnFromEscrowAsync(
                cancellationToken)
            .ConfigureAwait(false);
    }

    /*
     * ============================================================
     * Disable Acceptance
     * ============================================================
     */

    private async Task DisableAcceptanceAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        await _acceptanceGate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await DisableAcceptanceCoreAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _acceptanceGate
                .Release();
        }
    }

    private async Task DisableAcceptanceCoreAsync(
        CancellationToken cancellationToken)
    {
        EnsureConnected();

        try
        {
            await _restClient
                .DisableAcceptorAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            SetAcceptorState(
                CashAcceptorState.Inactive);
        }
        catch
        {
            SetAcceptorState(
                CashAcceptorState.Error);

            throw;
        }
    }

    /*
     * ============================================================
     * Polling
     * ============================================================
     */

    private void StartPolling()
    {
        if (
            _pollingTask is
            {
                IsCompleted: false
            })
        {
            throw new InvalidOperationException(
                "ITL status polling is already running.");
        }

        _pollingCancellation?
            .Dispose();

        _pollingCancellation =
            new CancellationTokenSource();

        _pollingTask =
            PollDeviceStatusAsync(
                _pollingCancellation.Token);
    }

    private async Task StopPollingAsync()
    {
        var cancellation =
            _pollingCancellation;

        var pollingTask =
            _pollingTask;

        if (
            cancellation is null ||
            pollingTask is null)
        {
            return;
        }

        cancellation
            .Cancel();

        try
        {
            await pollingTask
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            /*
             * Expected during explicit shutdown.
             */
        }
        finally
        {
            cancellation
                .Dispose();

            _pollingCancellation =
                null;

            _pollingTask =
                null;
        }
    }

    private async Task PollDeviceStatusAsync(
        CancellationToken cancellationToken)
    {
        var consecutiveFailures =
            0;

        while (
            _isConnected &&
            !cancellationToken
                .IsCancellationRequested)
        {
            try
            {
                var items =
                    await _restClient
                        .GetDeviceStatusAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                consecutiveFailures =
                    0;

                foreach (
                    var item
                    in items)
                {
                    ProcessDeviceStatusItem(
                        item);

                    if (
                        !_isConnected)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
                when (
                    cancellationToken
                        .IsCancellationRequested)
            {
                return;
            }
            catch (InvalidDataException exception)
            {
                FailPolling(
                    "ITL status polling received invalid device data: " +
                    exception.Message);

                return;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;

                if (
                    consecutiveFailures >=
                    _options
                        .MaximumConsecutivePollFailures)
                {
                    FailPolling(
                        "ITL status polling stopped after repeated failures: " +
                        exception.Message);

                    return;
                }

                if (
                    !_isConnected)
                {
                    return;
                }
            }

            await Task.Delay(
                    _options.PollInterval,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /*
     * ============================================================
     * Status Processing
     * ============================================================
     */

    private void ProcessDeviceStatusItem(
        DeviceStatusItemResponse item)
    {
        ArgumentNullException.ThrowIfNull(
            item);

        if (
            string.Equals(
                item.Type,
                "DeviceStatusResponse",
                StringComparison.OrdinalIgnoreCase))
        {
            ProcessDeviceState(
                item);

            return;
        }

        if (
            !string.IsNullOrWhiteSpace(
                item.Type) &&
            !string.Equals(
                item.Type,
                "CashEventResponse",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                item.Type,
                "NOTE_VALIDATOR",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var eventType =
            item.EventTypeAsString?
                .Trim()
                .ToUpperInvariant();

        switch (eventType)
        {
            case "ESCROW":
            case "NOTE_ESCROW":
            case "READ":
            case "NOTE_READ":
                {
                    HandleEscrowEvent(
                        item);

                    break;
                }

            case "STORED":
            case "STACKED":
            case "NOTE_STACKED":
            case "NOTE_STORED":
                {
                    HandleCommittedEvent();

                    break;
                }

            case "REJECTED":
            case "NOTE_REJECTED":
                {
                    HandleRejectedEvent();

                    break;
                }

            case "JAMMED":
            case "NOTE_JAMMED":
                {
                    SetAcceptorState(
                        CashAcceptorState.Error);

                    OnJam?
                        .Invoke(
                            this,
                            new CashRecyclerJamEventArgs(
                                item.Message ??
                                "ITL reported JAMMED."));

                    break;
                }

            case "STORED_FRAUD_ATTEMPT":
            case "STACKED_FRAUD_ATTEMPT":
            case "TIME_OUT":
            case "INCOMPLETE_PAYOUT":
            case "ERROR_DURING_PAYOUT":
                {
                    FailPolling(
                        "ITL reported a blocking cash event: " +
                        eventType +
                        ".");

                    break;
                }
        }
    }

    /*
     * ============================================================
     * Device State
     * ============================================================
     */

    private void ProcessDeviceState(
        DeviceStatusItemResponse item)
    {
        var state =
            item.StateAsString?
                .Trim()
                .ToUpperInvariant();

        switch (state)
        {
            case "NOT_CONNECTED":
            case "ERROR":
            case "DEVICE_FULL":
            case "MAINTENANCE_REQUIRED":
            case "CALIBRATION_FAILED":
            case "NOTE_PATH_OPEN":
                {
                    FailPolling(
                        "ITL reported a blocking device state: " +
                        state +
                        ".");

                    break;
                }

            case "JAM_RECOVERY":
            case "COIN_MECH_JAMMED":
                {
                    SetAcceptorState(
                        CashAcceptorState.Error);

                    OnJam?
                        .Invoke(
                            this,
                            new CashRecyclerJamEventArgs(
                                item.Message ??
                                "ITL reported " +
                                state +
                                "."));

                    break;
                }
        }
    }

    /*
     * ============================================================
     * Escrow Event
     * ============================================================
     */

    private void HandleEscrowEvent(
        DeviceStatusItemResponse item)
    {
        var note =
            CreateMoneyFromItlCashEvent(
                item);

        lock (_escrowSync)
        {
            if (
                _escrowedNote is not null)
            {
                if (
                    _escrowedNote.Value ==
                    note &&
                    _escrowCommandState ==
                    EscrowCommandState.None)
                {
                    /*
                     * Duplicate status notification for the same
                     * currently escrowed note.
                     */

                    return;
                }
            }

            _escrowedNote =
                note;

            _escrowCommandState =
                EscrowCommandState.None;
        }

        /*
         * Hardware events expose normalized Domain Money values.
         */

        OnNoteInserted?
            .Invoke(
                this,
                new NoteInsertedEventArgs(
                    note));

        OnNoteInEscrow?
            .Invoke(
                this,
                new NoteInEscrowEventArgs(
                    note));
    }

    /*
     * ============================================================
     * Commit Event
     * ============================================================
     */

    private void HandleCommittedEvent()
    {
        var note =
            CompleteEscrowCommand(
                EscrowCommandState.CommitRequested);

        if (
            note is not null)
        {
            OnEscrowResolved?
                .Invoke(
                    this,
                    new CashEscrowResolvedEventArgs(
                        note.Value,
                        CashEscrowResolution
                            .CommittedToVault));
        }
    }

    /*
     * ============================================================
     * Reject Event
     * ============================================================
     */

    private void HandleRejectedEvent()
    {
        var note =
            CompleteEscrowCommand(
                EscrowCommandState.ReturnRequested);

        if (
            note is not null)
        {
            OnEscrowResolved?
                .Invoke(
                    this,
                    new CashEscrowResolvedEventArgs(
                        note.Value,
                        CashEscrowResolution
                            .Rejected));
        }
        else
        {
            bool hasEscrowedNote;
            lock (_escrowSync)
            {
                hasEscrowedNote = _escrowedNote is not null;
            }

            if (!hasEscrowedNote)
            {
                OnEscrowResolved?
                    .Invoke(
                        this,
                        new CashEscrowResolvedEventArgs(
                            Money.Khr(0),
                            CashEscrowResolution
                                .Rejected));
            }
        }
    }

    /*
     * ============================================================
     * Escrow Tracking
     * ============================================================
     */

    private Money? CompleteEscrowCommand(
        EscrowCommandState expectedCommand)
    {
        lock (_escrowSync)
        {
            if (
                _escrowedNote is null)
            {
                return null;
            }

            if (
                _escrowCommandState !=
                expectedCommand)
            {
                return null;
            }

            var note =
                _escrowedNote.Value;

            _escrowedNote =
                null;

            _escrowCommandState =
                EscrowCommandState.None;

            return note;
        }
    }

    private void BeginEscrowCommand(
        EscrowCommandState commandState)
    {
        lock (_escrowSync)
        {
            if (
                _escrowedNote is null)
            {
                throw new InvalidOperationException(
                    "No ITL note is currently held in escrow.");
            }

            if (
                _escrowCommandState !=
                EscrowCommandState.None)
            {
                throw new InvalidOperationException(
                    "An escrow command has already been issued for the note.");
            }

            _escrowCommandState =
                commandState;
        }
    }

    /*
     * ============================================================
     * ITL Money Normalization
     * ============================================================
     *
     * CountryCode is authoritative for the inserted note.
     *
     * The physical REST dataset currently reports denomination
     * values at a scale of 100.
     *
     * USD:
     *
     *     raw 100 -> $1.00
     *     raw 500 -> $5.00
     *
     * KHR:
     *
     *     raw 50000   -> 500 KHR
     *     raw 100000  -> 1,000 KHR
     *     raw 500000  -> 5,000 KHR
     *     raw 1000000 -> 10,000 KHR
     *
     * No exchange-rate conversion is performed here.
     *
     * Exchange-rate conversion belongs to Core.
     * ============================================================
     */

    private Money CreateMoneyFromItlCashEvent(
        DeviceStatusItemResponse item)
    {
        if (
            !item.Value.HasValue ||
            item.Value.Value <=
                0m ||
            item.Value.Value !=
                decimal.Truncate(
                    item.Value.Value))
        {
            throw new InvalidDataException(
                "ITL cash event contained an invalid denomination value.");
        }

        var countryCode =
            item.CountryCode?
                .Trim()
                .ToUpperInvariant();

        if (
            string.IsNullOrWhiteSpace(
                countryCode))
        {
            throw new InvalidDataException(
                "ITL cash event did not contain a currency countryCode.");
        }

        if (
            !_options
                .IsCurrencyAllowed(
                    countryCode))
        {
            throw new InvalidDataException(
                "ITL cash event reported currency '" +
                countryCode +
                "', which is not enabled by the kiosk configuration.");
        }

        var rawValue =
            item.Value.Value;

        var normalizedValue =
            rawValue /
            ItlDenominationScale;

        Console.WriteLine($"[VendorXCashRecycler] Note detected by hardware: Raw={rawValue}, CountryCode={countryCode} -> Normalized={normalizedValue} {countryCode}");

        switch (countryCode)
        {
            case "USD":
                {
                    return Money.Usd(
                        normalizedValue);
                }

            case "KHR":
                {
                    /*
                     * Domain KHR represents whole riel only.
                     *
                     * Fail closed if the configured ITL dataset ever
                     * reports a scaled value that produces fractional
                     * KHR after normalization.
                     */

                    if (
                        normalizedValue !=
                        decimal.Truncate(
                            normalizedValue))
                    {
                        throw new InvalidDataException(
                            "ITL KHR denomination value '" +
                            rawValue +
                            "' normalized to fractional KHR '" +
                            normalizedValue +
                            "'.");
                    }

                    return Money.Khr(
                        normalizedValue);
                }

            default:
                {
                    throw new InvalidDataException(
                        "ITL cash event reported unsupported currency '" +
                        countryCode +
                        "'.");
                }
        }
    }

    /*
     * ============================================================
     * OpenConnection Validation
     * ============================================================
     */

    private static void ValidateOpenConnection(
        OpenConnectionResponse response)
    {
        if (
            response.IsOpen !=
            true)
        {
            throw new InvalidDataException(
                "ITL OpenConnection did not confirm that the physical " +
                "device is open.");
        }

        if (
            !string.IsNullOrWhiteSpace(
                response.DeviceError) &&
            !string.Equals(
                response.DeviceError,
                "NONE",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ITL OpenConnection reported device error '" +
                response.DeviceError +
                "'.");
        }

        /*
         * Fail closed:
         *
         * acceptance OFF
         * automatic escrow OFF
         * payout OFF
         */

        if (
            response.AcceptorEnabled ==
                true ||
            response.AutoAcceptEscrowEnabled ==
                true ||
            response.PayoutEnabled ==
                true)
        {
            throw new InvalidOperationException(
                "ITL OpenConnection did not honor the fail-closed " +
                "acceptance, escrow, and payout settings.");
        }
    }

    /*
     * ============================================================
     * Reset Escrow
     * ============================================================
     */

    private void ResetEscrowTracking()
    {
        lock (_escrowSync)
        {
            _escrowedNote =
                null;

            _escrowCommandState =
                EscrowCommandState.None;
        }
    }

    /*
     * ============================================================
     * Connection Guard
     * ============================================================
     */

    private void EnsureConnected()
    {
        if (
            !_isConnected ||
            !_restClient
                .HasOpenDevice)
        {
            throw new InvalidOperationException(
                "The ITL cash recycler is not connected.");
        }
    }

    /*
     * ============================================================
     * Poll Failure
     * ============================================================
     */

    private void FailPolling(
        string message)
    {
        _isConnected =
            false;

        SetAcceptorState(
            CashAcceptorState.Error);

        RaiseFault(
            message);
    }

    /*
     * ============================================================
     * Fault
     * ============================================================
     */

    private void RaiseFault(
        string message)
    {
        OnFault?
            .Invoke(
                this,
                new HardwareFaultEventArgs(
                    "CashRecycler",
                    message));
    }

    /*
     * ============================================================
     * Acceptor State
     * ============================================================
     */

    private void SetAcceptorState(
        CashAcceptorState state)
    {
        lock (_stateSync)
        {
            if (
                _acceptorState ==
                state)
            {
                return;
            }

            _acceptorState =
                state;
        }

        OnAcceptorStateChanged?
            .Invoke(
                this,
                new CashAcceptorStateChangedEventArgs(
                    state));
    }

    /*
     * ============================================================
     * Escrow Command State
     * ============================================================
     */

    private enum EscrowCommandState
    {
        None = 0,

        CommitRequested = 1,

        ReturnRequested = 2
    }
}