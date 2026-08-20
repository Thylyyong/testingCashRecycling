using System.Collections.Concurrent;
using System.Net;
using System.Text;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;
using Xunit;

namespace SelfCheckoutKiosk_Integration_Tests;

public sealed class CashRecyclerXTests
{
    [Fact]
    public async Task Connect_AuthenticatesAndOpensFailClosed()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);

        await recycler.ConnectAsync();
        await WaitForRequestAsync(handler, "/api/CashDevice/GetDeviceStatus");

        Assert.True(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Inactive, recycler.AcceptorState);

        var authentication = Assert.Single(
            handler.Requests,
            request => request.Path == "/api/Users/Authenticate");
        Assert.Contains("\"Username\":\"operator\"", authentication.Body);
        Assert.Contains("\"Password\":\"unit-test-password\"", authentication.Body);

        var open = Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/OpenConnection");
        Assert.Equal("Bearer", open.AuthorizationScheme);
        Assert.Equal("unit-test-token", open.AuthorizationParameter);
        Assert.Contains("\"ComPort\":\"COM7\"", open.Body);
        Assert.Contains("\"EnableAcceptor\":false", open.Body);
        Assert.Contains("\"EnableAutoAcceptEscrow\":false", open.Body);
        Assert.Contains("\"EnablePayout\":false", open.Body);

        var status = Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/GetDeviceStatus");
        Assert.Contains("deviceID=DEVICE-7", status.Query);

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task Connect_AuthenticationFailureIsFailClosedAndRedacted()
    {
        using var handler = new CashDeviceApiHandler
        {
            AuthenticationStatus = HttpStatusCode.Unauthorized,
            AuthenticationBody =
                "{\"message\":\"unit-test-password unit-test-token\"}"
        };
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => recycler.ConnectAsync());

        Assert.False(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Error, recycler.AcceptorState);
        Assert.DoesNotContain("unit-test-password", exception.Message);
        Assert.DoesNotContain("unit-test-token", exception.Message);
        Assert.DoesNotContain(
            handler.AuthenticationBody,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_PortErrorIsReportedAndDoesNotStartPolling()
    {
        using var handler = new CashDeviceApiHandler
        {
            OpenStatus = HttpStatusCode.BadRequest,
            OpenBody = "{\"error\":\"PORT_ERROR\"}"
        };
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => recycler.ConnectAsync());

        Assert.Contains("PORT_ERROR", exception.Message);
        Assert.False(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Error, recycler.AcceptorState);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path == "/api/CashDevice/GetDeviceStatus");
    }

    [Fact]
    public async Task ArmAndDisarm_PublishReadinessOnlyAfterSuccessfulCommands()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var states = new List<CashAcceptorState>();
        recycler.OnAcceptorStateChanged +=
            (_, eventArgs) => states.Add(eventArgs.State);

        await recycler.ConnectAsync();
        await recycler.ArmAcceptanceAsync();

        Assert.Equal(
            new[]
            {
                CashAcceptorState.Activating,
                CashAcceptorState.Ready
            },
            states);
        Assert.Equal(CashAcceptorState.Ready, recycler.AcceptorState);

        var autoAccept = Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/SetAutoAccept");
        Assert.Equal("false", autoAccept.Body);
        Assert.Contains("deviceID=DEVICE-7", autoAccept.Query);
        Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/EnableAcceptor");

        await recycler.DisarmAcceptanceAsync();

        Assert.Equal(CashAcceptorState.Inactive, recycler.AcceptorState);
        Assert.Equal(CashAcceptorState.Inactive, states[^1]);
        Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/DisableAcceptor");

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task Arm_EnableFailureNeverPublishesReady()
    {
        using var handler = new CashDeviceApiHandler
        {
            EnableStatus = HttpStatusCode.ServiceUnavailable
        };
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var states = new List<CashAcceptorState>();
        recycler.OnAcceptorStateChanged +=
            (_, eventArgs) => states.Add(eventArgs.State);

        await recycler.ConnectAsync();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => recycler.ArmAcceptanceAsync());

        Assert.Equal(CashAcceptorState.Error, recycler.AcceptorState);
        Assert.Contains(CashAcceptorState.Activating, states);
        Assert.Contains(CashAcceptorState.Error, states);
        Assert.DoesNotContain(CashAcceptorState.Ready, states);
        Assert.Contains(
            handler.Requests,
            request => request.Path == "/api/CashDevice/DisableAcceptor");

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task Poll_DisconnectedDeviceRaisesFaultAndClearsReadiness()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var faultRaised = NewSignal<HardwareFaultEventArgs>();
        recycler.OnFault +=
            (_, eventArgs) => faultRaised.TrySetResult(eventArgs);

        await recycler.ConnectAsync();
        await recycler.ArmAcceptanceAsync();
        Assert.Equal(CashAcceptorState.Ready, recycler.AcceptorState);
        handler.EnqueueStatus(
            "[{\"type\":\"DeviceStatusResponse\"," +
            "\"stateAsString\":\"NOT_CONNECTED\"}]");
        var fault = await faultRaised.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Error, recycler.AcceptorState);
        Assert.Contains("NOT_CONNECTED", fault.Message);

        await recycler.DisconnectAsync();
    }

    [Theory]
    [InlineData("STORED")]
    [InlineData("STACKED")]
    public async Task EscrowCommit_MapsUsdMinorUnitsAndWaitsForStoredEvent(
        string completionEvent)
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var inserted = NewSignal<NoteInsertedEventArgs>();
        var escrowed = NewSignal<NoteInEscrowEventArgs>();
        var resolved = NewSignal<CashEscrowResolvedEventArgs>();
        var resolutionCount = 0;
        recycler.OnNoteInserted +=
            (_, eventArgs) => inserted.TrySetResult(eventArgs);
        recycler.OnNoteInEscrow +=
            (_, eventArgs) => escrowed.TrySetResult(eventArgs);
        recycler.OnEscrowResolved += (_, eventArgs) =>
        {
            Interlocked.Increment(ref resolutionCount);
            resolved.TrySetResult(eventArgs);
        };

        await recycler.ConnectAsync();
        handler.EnqueueStatus(CashEvent("ESCROW", 500, "USD"));

        var insertedNote = await inserted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var escrowedNote = await escrowed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(Money.Usd(5m), insertedNote.Note);
        Assert.Equal(Money.Usd(5m), escrowedNote.Note);

        await recycler.CommitEscrowedNoteAsync();
        Assert.False(resolved.Task.IsCompleted);
        handler.EnqueueStatus(CashEvent(completionEvent, 500, "USD"));

        var outcome = await resolved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(Money.Usd(5m), outcome.Note);
        Assert.Equal(
            CashEscrowResolution.CommittedToVault,
            outcome.Resolution);
        Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/AcceptFromEscrow");

        handler.EnqueueStatus(CashEvent(
            completionEvent == "STORED" ? "STACKED" : "STORED",
            500,
            "USD"));
        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref resolutionCount));

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task EscrowReturn_MapsRejectedOnlyAfterReturnCommand()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var escrowed = NewSignal<NoteInEscrowEventArgs>();
        var resolved = NewSignal<CashEscrowResolvedEventArgs>();
        recycler.OnNoteInEscrow +=
            (_, eventArgs) => escrowed.TrySetResult(eventArgs);
        recycler.OnEscrowResolved +=
            (_, eventArgs) => resolved.TrySetResult(eventArgs);

        await recycler.ConnectAsync();
        handler.EnqueueStatus(CashEvent("ESCROW", 1000, "USD"));
        await escrowed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        handler.EnqueueStatus(CashEvent("REJECTED", 1000, "USD"));
        await Task.Delay(50);
        Assert.False(resolved.Task.IsCompleted);

        await recycler.RejectEscrowedNoteAsync();
        handler.EnqueueStatus(CashEvent("REJECTED", 1000, "USD"));

        var outcome = await resolved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(Money.Usd(10m), outcome.Note);
        Assert.Equal(CashEscrowResolution.Rejected, outcome.Resolution);
        Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/ReturnFromEscrow");

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task Poll_DuplicateEscrowEventIsIgnoredUntilResolution()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var escrowed = NewSignal<NoteInEscrowEventArgs>();
        var resolved = NewSignal<CashEscrowResolvedEventArgs>();
        var insertedCount = 0;
        var escrowCount = 0;
        recycler.OnNoteInserted +=
            (_, _) => Interlocked.Increment(ref insertedCount);
        recycler.OnNoteInEscrow += (_, eventArgs) =>
        {
            Interlocked.Increment(ref escrowCount);
            escrowed.TrySetResult(eventArgs);
        };
        recycler.OnEscrowResolved +=
            (_, eventArgs) => resolved.TrySetResult(eventArgs);

        await recycler.ConnectAsync();
        handler.EnqueueStatus(CashEvent("ESCROW", 100, "USD"));
        handler.EnqueueStatus(CashEvent("ESCROW", 100, "USD"));
        await escrowed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        Assert.Equal(1, Volatile.Read(ref insertedCount));
        Assert.Equal(1, Volatile.Read(ref escrowCount));

        await recycler.RejectEscrowedNoteAsync();
        handler.EnqueueStatus(CashEvent("REJECTED", 100, "USD"));
        await resolved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task Poll_JamPublishesJamAndErrorState()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var jamRaised = NewSignal<CashRecyclerJamEventArgs>();
        recycler.OnJam +=
            (_, eventArgs) => jamRaised.TrySetResult(eventArgs);

        await recycler.ConnectAsync();
        handler.EnqueueStatus(
            "[{\"type\":\"CashEventResponse\"," +
            "\"eventTypeAsString\":\"JAMMED\"," +
            "\"message\":\"Note path blocked\"}]");

        var jam = await jamRaised.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Note path blocked", jam.Message);
        Assert.Equal(CashAcceptorState.Error, recycler.AcceptorState);

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task Poll_MalformedJsonStopsAfterConfiguredFailureLimit()
    {
        using var handler = new CashDeviceApiHandler();
        handler.EnqueueStatus("{");
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(
            httpClient,
            maximumConsecutivePollFailures: 1);
        var faultRaised = NewSignal<HardwareFaultEventArgs>();
        recycler.OnFault +=
            (_, eventArgs) => faultRaised.TrySetResult(eventArgs);

        await recycler.ConnectAsync();
        var fault = await faultRaised.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Error, recycler.AcceptorState);
        Assert.Contains("malformed JSON", fault.Message);

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task Connect_RestUnavailableRaisesFaultWithoutFallback()
    {
        using var handler = new CashDeviceApiHandler
        {
            ThrowDuringAuthentication = true
        };
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var faultRaised = NewSignal<HardwareFaultEventArgs>();
        recycler.OnFault +=
            (_, eventArgs) => faultRaised.TrySetResult(eventArgs);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => recycler.ConnectAsync());
        var fault = await faultRaised.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Error, recycler.AcceptorState);
        Assert.Contains("could not be connected", fault.Message);
    }

    [Fact]
    public async Task Connect_HttpTimeoutRaisesTimeoutWithoutFallback()
    {
        using var handler = new CashDeviceApiHandler
        {
            HangDuringAuthentication = true
        };
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(
            httpClient,
            requestTimeout: TimeSpan.FromMilliseconds(40));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => recycler.ConnectAsync());

        Assert.Contains("Authenticate", exception.Message);
        Assert.False(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Error, recycler.AcceptorState);
    }

    [Fact]
    public async Task Disconnect_CancelsPollingAndCallsDeviceEndpoint()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);

        await recycler.ConnectAsync();
        await WaitForRequestAsync(handler, "/api/CashDevice/GetDeviceStatus");
        await recycler.DisconnectAsync();
        var statusCountAtDisconnect = handler.Requests.Count(
            request => request.Path == "/api/CashDevice/GetDeviceStatus");

        await Task.Delay(50);

        Assert.False(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Inactive, recycler.AcceptorState);
        Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/DisconnectDevice");
        Assert.Equal(
            statusCountAtDisconnect,
            handler.Requests.Count(
                request => request.Path == "/api/CashDevice/GetDeviceStatus"));
    }

    [Fact]
    public async Task Disconnect_WithEscrowedNoteDisablesButKeepsPollingForResolution()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);
        var escrowed = NewSignal<NoteInEscrowEventArgs>();
        var resolved = NewSignal<CashEscrowResolvedEventArgs>();
        recycler.OnNoteInEscrow +=
            (_, eventArgs) => escrowed.TrySetResult(eventArgs);
        recycler.OnEscrowResolved +=
            (_, eventArgs) => resolved.TrySetResult(eventArgs);

        await recycler.ConnectAsync();
        await recycler.ArmAcceptanceAsync();
        handler.EnqueueStatus(CashEvent("ESCROW", 100, "USD"));
        await escrowed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => recycler.DisconnectAsync());

        Assert.Contains("escrow resolution", exception.Message);
        Assert.True(recycler.IsConnected);
        Assert.Equal(CashAcceptorState.Inactive, recycler.AcceptorState);
        Assert.Contains(
            handler.Requests,
            request => request.Path == "/api/CashDevice/DisableAcceptor");
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path == "/api/CashDevice/DisconnectDevice");

        await recycler.RejectEscrowedNoteAsync();
        handler.EnqueueStatus(CashEvent("REJECTED", 100, "USD"));
        await resolved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task ConcurrentConnect_AuthenticatesAndOpensOnlyOnce()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);

        await Task.WhenAll(
            recycler.ConnectAsync(),
            recycler.ConnectAsync());

        Assert.Single(
            handler.Requests,
            request => request.Path == "/api/Users/Authenticate");
        Assert.Single(
            handler.Requests,
            request => request.Path == "/api/CashDevice/OpenConnection");

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task Dispense_RemainsDisabledForPhysicalHardware()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = CreateRecycler(httpClient);

        await recycler.ConnectAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => recycler.DispenseAsync(ChangeBreakdown.Empty));

        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains(
                "Payout",
                StringComparison.OrdinalIgnoreCase));

        await recycler.DisconnectAsync();
    }

    [Fact]
    public async Task EscrowCommit_DualCurrency_AcceptsBothUsdAndKhrNotes()
    {
        using var handler = new CashDeviceApiHandler();
        using var httpClient = new HttpClient(handler);
        var recycler = new VendorXCashRecycler(
            httpClient,
            new CashRecyclerXOptions
            {
                BaseUrl = "http://cashdevice.test/",
                Username = "operator",
                Password = "unit-test-password",
                ComPort = "COM7",
                Currency = "USD,KHR",
                SspAddress = 0,
                PollInterval = TimeSpan.FromMilliseconds(10),
                RequestTimeout = TimeSpan.FromSeconds(1)
            });

        var insertedSignals = new List<NoteInsertedEventArgs>();
        var escrowedSignals = new List<NoteInEscrowEventArgs>();
        var resolvedSignals = new List<CashEscrowResolvedEventArgs>();

        var usdEscrowed = NewSignal<NoteInEscrowEventArgs>();
        var khrEscrowed = NewSignal<NoteInEscrowEventArgs>();
        var usdResolved = NewSignal<CashEscrowResolvedEventArgs>();
        var khrResolved = NewSignal<CashEscrowResolvedEventArgs>();

        recycler.OnNoteInEscrow += (_, e) =>
        {
            if (e.Note.Currency == SelfCheckoutKiosk.Domain.Enums.CurrencyCode.Usd)
                usdEscrowed.TrySetResult(e);
            else
                khrEscrowed.TrySetResult(e);
        };

        recycler.OnEscrowResolved += (_, e) =>
        {
            if (e.Note.Currency == SelfCheckoutKiosk.Domain.Enums.CurrencyCode.Usd)
                usdResolved.TrySetResult(e);
            else
                khrResolved.TrySetResult(e);
        };

        await recycler.ConnectAsync();

        // 1. Send USD note ($1.00 -> raw value 100)
        handler.EnqueueStatus(CashEvent("ESCROW", 100, "USD"));
        var usdNote = await usdEscrowed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(Money.Usd(1m), usdNote.Note);

        await recycler.CommitEscrowedNoteAsync();
        handler.EnqueueStatus(CashEvent("STORED", 100, "USD"));
        var usdOutcome = await usdResolved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(Money.Usd(1m), usdOutcome.Note);
        Assert.Equal(CashEscrowResolution.CommittedToVault, usdOutcome.Resolution);

        // 2. Send KHR note (10,000 KHR -> raw value 1000000)
        handler.EnqueueStatus(CashEvent("ESCROW", 1000000, "KHR"));
        var khrNote = await khrEscrowed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(Money.Khr(10000m), khrNote.Note);

        await recycler.CommitEscrowedNoteAsync();
        handler.EnqueueStatus(CashEvent("STACKED", 1000000, "KHR"));
        var khrOutcome = await khrResolved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(Money.Khr(10000m), khrOutcome.Note);
        Assert.Equal(CashEscrowResolution.CommittedToVault, khrOutcome.Resolution);

        await recycler.DisconnectAsync();
    }

    private static VendorXCashRecycler CreateRecycler(
        HttpClient httpClient,
        TimeSpan? requestTimeout = null,
        int maximumConsecutivePollFailures = 2)
    {
        return new VendorXCashRecycler(
            httpClient,
            new CashRecyclerXOptions
            {
                BaseUrl = "http://cashdevice.test/",
                Username = "operator",
                Password = "unit-test-password",
                ComPort = "COM7",
                Currency = "USD",
                SspAddress = 0,
                PollInterval = TimeSpan.FromMilliseconds(10),
                RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(1),
                MaximumConsecutivePollFailures =
                    maximumConsecutivePollFailures
            });
    }

    private static TaskCompletionSource<T> NewSignal<T>()
    {
        return new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static string CashEvent(
        string eventType,
        int value,
        string countryCode)
    {
        return
            "[{\"type\":\"CashEventResponse\"," +
            "\"eventTypeAsString\":\"" + eventType + "\"," +
            "\"value\":" + value + "," +
            "\"countryCode\":\"" + countryCode + "\"}]";
    }

    private static async Task WaitForRequestAsync(
        CashDeviceApiHandler handler,
        string path)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (handler.Requests.Any(request => request.Path == path))
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException(
            "The expected mocked CashDevice request was not observed: " + path);
    }

    private sealed class CashDeviceApiHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _statusPayloads = new();

        public ConcurrentBag<RecordedRequest> Requests { get; } = new();

        public HttpStatusCode AuthenticationStatus { get; init; } =
            HttpStatusCode.OK;

        public string AuthenticationBody { get; init; } =
            "{\"token\":\"unit-test-token\"}";

        public HttpStatusCode OpenStatus { get; init; } = HttpStatusCode.OK;

        public string OpenBody { get; init; } =
            "{\"deviceID\":\"DEVICE-7\",\"isOpen\":true," +
            "\"deviceError\":\"NONE\",\"acceptorEnabled\":false," +
            "\"autoAcceptEscrowEnabled\":false,\"payoutEnabled\":false}";

        public HttpStatusCode EnableStatus { get; init; } = HttpStatusCode.OK;

        public bool ThrowDuringAuthentication { get; init; }

        public bool HangDuringAuthentication { get; init; }

        public void EnqueueStatus(string json)
        {
            _statusPayloads.Enqueue(json);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content
                    .ReadAsStringAsync(cancellationToken);

            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.RequestUri?.Query ?? string.Empty,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body);
            Requests.Add(recorded);

            if (recorded.Path == "/api/Users/Authenticate")
            {
                if (ThrowDuringAuthentication)
                {
                    throw new HttpRequestException(
                        "Mock CashDevice service is unavailable.");
                }

                if (HangDuringAuthentication)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return JsonResponse(AuthenticationStatus, AuthenticationBody);
            }

            if (recorded.Path == "/api/CashDevice/OpenConnection")
            {
                return JsonResponse(OpenStatus, OpenBody);
            }

            if (recorded.Path == "/api/CashDevice/GetDeviceStatus")
            {
                return JsonResponse(
                    HttpStatusCode.OK,
                    _statusPayloads.TryDequeue(out var status)
                        ? status
                        : "[]");
            }

            if (recorded.Path == "/api/CashDevice/EnableAcceptor")
            {
                return JsonResponse(EnableStatus, "{}");
            }

            return JsonResponse(HttpStatusCode.OK, "{}");
        }

        private static HttpResponseMessage JsonResponse(
            HttpStatusCode statusCode,
            string json)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Body);
}
