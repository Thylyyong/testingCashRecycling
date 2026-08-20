using System.Net.Http.Json;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Hal.Vendor.ItlRestCashRecycler;

/// <summary>
/// <see cref="ICashRecycler"/> for the ITL bill validator, talking to the
/// local "CashDevice-RestAPI" bridge over HTTP on localhost — no vendor SDK
/// is linked into this process at all; the bridge is a separate local
/// service that owns the actual serial/USB link to the device.
///
/// Event translation is a background POLLING loop (not a WebSocket) against
/// GET /api/device/status: simpler, no extra AOT-sensitive networking
/// surface, and entirely adequate at a ~250ms interval for a peripheral this
/// local. <see cref="DeviceStatusDto.SequenceId"/> is what makes polling
/// safe — the loop only reacts when it advances, so two polls landing on the
/// same physical event never double-fire <see cref="OnNoteInEscrow"/>.
///
/// The <see cref="HttpClient"/> is constructor-injected and NOT owned here
/// (composition root sets BaseAddress and owns disposal) — consistent with
/// <c>HttpErpSyncClient</c> in Infrastructure.Sync.
/// </summary>
public sealed class ItlRestCashRecycler : ICashRecycler, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CassettePollInterval = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _pollingCts = new();

    private long _lastSeenSequenceId = -1;
    private DateTimeOffset _lastCassettePollUtc = DateTimeOffset.MinValue;
    private bool _escrowOpen;

#pragma warning disable CS0067
    public event EventHandler<NoteInsertedEventArgs>? OnNoteInserted;
    public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
    public event EventHandler<HardwareFaultEventArgs>? OnFault;
    public event EventHandler<CashAcceptorStateChangedEventArgs>? OnAcceptorStateChanged;
    public event EventHandler<CashRecyclerJamEventArgs>? OnJam;
    public event EventHandler<CassetteInventoryChangedEventArgs>? OnCassetteInventoryChanged;
#pragma warning restore CS0067

    public ItlRestCashRecycler(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient
            .PostAsync("api/device/initialize", content: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Prime the sequence cursor to "now" so the poller doesn't replay
        // whatever the bridge already logged before this process started.
        DeviceStatusDto initialStatus = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        _lastSeenSequenceId = initialStatus.SequenceId;

        await RefreshCassetteLevelsAsync(cancellationToken).ConfigureAwait(false);

        _ = Task.Run(() => PollingLoopAsync(_pollingCts.Token), CancellationToken.None);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _pollingCts.Cancel();
        return Task.CompletedTask;
    }

    public async Task ArmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient
            .PostAsync("api/device/accept/enable", content: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient
            .PostAsync("api/device/accept/disable", content: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Low-float lockout hard-stop — same REST call as disarm; the
    /// bridge exposes one "stop accepting" verb, not a separate emergency one.</summary>
    public Task StopAcceptingCashAsync(CancellationToken cancellationToken = default)
        => DisarmAcceptanceAsync(cancellationToken);

    public async Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient
            .PostAsync("api/device/accept/reject", content: null, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _escrowOpen = false;
    }

    public async Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken cancellationToken = default)
    {
        var request = new DispenseRequestDto(
            ToNoteDtos(change.UsdNotes),
            ToNoteDtos(change.KhrNotes));

        using HttpResponseMessage response = await _httpClient
            .PostAsJsonAsync("api/device/dispense", request, CashDeviceRestJsonContext.Default.DispenseRequestDto, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        DispenseResponseDto? result = await response.Content
            .ReadFromJsonAsync(CashDeviceRestJsonContext.Default.DispenseResponseDto, cancellationToken)
            .ConfigureAwait(false);

        // A dispense always changes cassette levels — refresh immediately
        // rather than waiting for the next periodic poll to catch up.
        await RefreshCassetteLevelsAsync(cancellationToken).ConfigureAwait(false);

        if (result is null)
            return new DispenseResult(false, ChangeBreakdown.Empty);

        var dispensed = new ChangeBreakdown(
            ToMoneyPairs(result.DispensedUsdNotes, CurrencyCode.Usd),
            ToMoneyPairs(result.DispensedKhrNotes, CurrencyCode.Khr));

        return new DispenseResult(result.Success, dispensed);
    }

    // ---- background status polling -----------------------------------------

    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                DeviceStatusDto status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
                HandleStatus(status);

                if (DateTimeOffset.UtcNow - _lastCassettePollUtc >= CassettePollInterval)
                    await RefreshCassetteLevelsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The bridge being briefly unreachable (localhost service
                // restart, USB hiccup) IS a hardware fault the engine needs
                // to hear about — it must never silently stop polling.
                OnFault?.Invoke(this, new HardwareFaultEventArgs("ItlRestCashRecycler", ex.Message));
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void HandleStatus(DeviceStatusDto status)
    {
        if (status.SequenceId <= _lastSeenSequenceId) return; // no new event since last poll
        _lastSeenSequenceId = status.SequenceId;

        switch (status.State)
        {
            case "NoteInEscrow" when status.EscrowedNote is { } note && !_escrowOpen:
                _escrowOpen = true;
                OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(ToMoney(note)));
                break;

            case "Faulted":
                _escrowOpen = false;
                OnFault?.Invoke(this, new HardwareFaultEventArgs("ItlRestCashRecycler", status.FaultMessage ?? "Unspecified device fault."));
                break;

            case "Idle":
                _escrowOpen = false;
                break;
        }
    }

    private async Task<DeviceStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        DeviceStatusDto? status = await _httpClient
            .GetFromJsonAsync("api/device/status", CashDeviceRestJsonContext.Default.DeviceStatusDto, cancellationToken)
            .ConfigureAwait(false);

        return status ?? throw new InvalidOperationException("CashDevice-RestAPI returned an empty status payload.");
    }

    private async Task RefreshCassetteLevelsAsync(CancellationToken cancellationToken)
    {
        CassetteLevelsDto? levels = await _httpClient
            .GetFromJsonAsync("api/device/cassettes", CashDeviceRestJsonContext.Default.CassetteLevelsDto, cancellationToken)
            .ConfigureAwait(false);

        _lastCassettePollUtc = DateTimeOffset.UtcNow;
        if (levels is null) return;

        var counts = new Dictionary<int, int>();
        foreach (CassetteLevelDto level in levels.Levels)
            counts[level.DenominationKhr] = level.Count;

        OnCassetteInventoryChanged?.Invoke(this, new CassetteInventoryChangedEventArgs(counts));
    }

    // ---- DTO <-> Domain mapping ---------------------------------------------

    private static IReadOnlyList<DispenseNoteDto> ToNoteDtos(IReadOnlyList<KeyValuePair<Money, int>> notes)
    {
        var result = new List<DispenseNoteDto>(notes.Count);
        foreach (KeyValuePair<Money, int> note in notes)
            result.Add(new DispenseNoteDto((int)note.Key.Amount, note.Value));
        return result;
    }

    private static IReadOnlyList<KeyValuePair<Money, int>> ToMoneyPairs(IReadOnlyList<DispenseNoteDto> notes, CurrencyCode currency)
    {
        var result = new List<KeyValuePair<Money, int>>(notes.Count);
        foreach (DispenseNoteDto note in notes)
        {
            Money money = currency == CurrencyCode.Usd ? Money.Usd(note.Denomination) : Money.Khr(note.Denomination);
            result.Add(new KeyValuePair<Money, int>(money, note.Count));
        }
        return result;
    }

    private static Money ToMoney(NoteDto note) => note.CurrencyCode.Equals("Usd", StringComparison.OrdinalIgnoreCase)
        ? Money.Usd(note.Denomination)
        : Money.Khr(note.Denomination);

    public void Dispose()
    {
        _pollingCts.Cancel();
        _pollingCts.Dispose();
    }
}
