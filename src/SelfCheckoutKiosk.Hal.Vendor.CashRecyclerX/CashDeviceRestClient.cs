using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Owns all ITL CashDevice REST endpoint, bearer-token,
/// and deviceID details.
/// </summary>
internal sealed class CashDeviceRestClient
{
    /*
     * ============================================================
     * Endpoints
     * ============================================================
     */

    private const string AuthenticateEndpoint =
        "api/Users/Authenticate";

    private const string OpenConnectionEndpoint =
        "api/CashDevice/OpenConnection";

    private const string DeviceStatusEndpoint =
        "api/CashDevice/GetDeviceStatus";

    private const string SetAutoAcceptEndpoint =
        "api/CashDevice/SetAutoAccept";

    private const string EnableAcceptorEndpoint =
        "api/CashDevice/EnableAcceptor";

    private const string DisableAcceptorEndpoint =
        "api/CashDevice/DisableAcceptor";

    private const string AcceptFromEscrowEndpoint =
        "api/CashDevice/AcceptFromEscrow";

    private const string ReturnFromEscrowEndpoint =
        "api/CashDevice/ReturnFromEscrow";

    private const string DisconnectDeviceEndpoint =
        "api/CashDevice/DisconnectDevice";


    /*
     * ============================================================
     * Dependencies / Session
     * ============================================================
     */

    private readonly HttpClient
        _httpClient;

    private readonly CashRecyclerXOptions
        _options;

    private readonly Uri
        _baseUri;


    private string?
        _bearerToken;

    private string?
        _deviceId;


    /*
     * ============================================================
     * Constructor
     * ============================================================
     */

    public CashDeviceRestClient(
        HttpClient httpClient,
        CashRecyclerXOptions options)
    {
        ArgumentNullException.ThrowIfNull(
            httpClient);

        ArgumentNullException.ThrowIfNull(
            options);


        _httpClient =
            httpClient;

        _options =
            options;

        _baseUri =
            options
                .ValidateAndCreateBaseUri();
    }


    /*
     * ============================================================
     * Session State
     * ============================================================
     */

    public string?
        DeviceId =>
            _deviceId;


    public bool
        HasOpenDevice =>
            !string.IsNullOrWhiteSpace(
                _deviceId);


    /*
     * ============================================================
     * Authenticate
     * ============================================================
     */

    public async Task AuthenticateAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Authentication starts a fresh REST session.
         */

        _bearerToken =
            null;

        _deviceId =
            null;


        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                CreateUri(
                    AuthenticateEndpoint))
            {
                Content =
                    JsonContent.Create(
                        new AuthenticateRequest(
                            _options.Username,
                            _options.Password),
                        CashDeviceJsonContext
                            .Default
                            .AuthenticateRequest)
            };


        var response =
            await SendAndReadAsync(
                    request,
                    "Authenticate",
                    cancellationToken)
                .ConfigureAwait(false);


        if (
            !response.IsSuccessStatusCode
        )
        {
            throw CreateRequestFailure(
                "Authenticate",
                response.StatusCode,
                response.Content,
                includeResponseReason:
                    false);
        }


        AuthenticateResponse?
            result;


        try
        {
            result =
                JsonSerializer.Deserialize(
                    response.Content,
                    CashDeviceJsonContext
                        .Default
                        .AuthenticateResponse);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "CashDevice authentication returned malformed JSON.",
                exception);
        }


        if (
            string.IsNullOrWhiteSpace(
                result?.Token)
        )
        {
            throw new InvalidDataException(
                "CashDevice authentication did not return a bearer token.");
        }


        _bearerToken =
            result.Token;
    }


    /*
     * ============================================================
     * Open Connection
     * ============================================================
     */

    public async Task<OpenConnectionResponse>
        OpenConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        /*
         * Fail-closed startup:
         *
         * - Acceptor disabled
         * - Automatic escrow disabled
         * - Payout disabled
         *
         * The engine explicitly enables acceptance when cash payment
         * begins.
         */

        var body =
            new OpenConnectionRequest
            {
                ComPort =
                    _options.ComPort,

                SspAddress =
                    _options.SspAddress,

                EncryptionKey =
                    _options.EncryptionKey,

                LogFilePath =
                    _options.DeviceLogFilePath,

                EnableAcceptor =
                    false,

                EnableAutoAcceptEscrow =
                    false,

                EnablePayout =
                    false
            };


        using var request =
            CreateAuthorizedRequest(
                HttpMethod.Post,
                OpenConnectionEndpoint);


        request.Content =
            JsonContent.Create(
                body,
                CashDeviceJsonContext
                    .Default
                    .OpenConnectionRequest);


        var response =
            await SendAndReadAsync(
                    request,
                    "OpenConnection",
                    cancellationToken)
                .ConfigureAwait(false);


        OpenConnectionResponse?
            result =
                null;


        if (
            !string.IsNullOrWhiteSpace(
                response.Content)
        )
        {
            try
            {
                result =
                    JsonSerializer.Deserialize(
                        response.Content,
                        CashDeviceJsonContext
                            .Default
                            .OpenConnectionResponse);
            }
            catch (JsonException exception)
                when (
                    !response
                        .IsSuccessStatusCode
                )
            {
                /*
                 * Some ITL error responses are plain text rather than
                 * the normal OpenConnection response object.
                 */

                _ =
                    exception;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "CashDevice OpenConnection returned malformed JSON.",
                    exception);
            }
        }


        if (
            !response.IsSuccessStatusCode
        )
        {
            // If the device or port was left open from a prior session, try to disconnect first and retry once
            try
            {
                using var disconnectReq = CreateAuthorizedRequest(HttpMethod.Post, DisconnectDeviceEndpoint);
                var discResp = await SendAndReadAsync(disconnectReq, "DisconnectDevice", cancellationToken).ConfigureAwait(false);
                if (discResp.IsSuccessStatusCode)
                {
                    await Task.Delay(300, cancellationToken).ConfigureAwait(false);

                    using var retryRequest = CreateAuthorizedRequest(HttpMethod.Post, OpenConnectionEndpoint);
                    retryRequest.Content = JsonContent.Create(body, CashDeviceJsonContext.Default.OpenConnectionRequest);
                    var retryResponse = await SendAndReadAsync(retryRequest, "OpenConnection", cancellationToken).ConfigureAwait(false);

                    if (retryResponse.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(retryResponse.Content))
                    {
                        var retryResult = JsonSerializer.Deserialize(retryResponse.Content, CashDeviceJsonContext.Default.OpenConnectionResponse);
                        if (retryResult != null && !string.IsNullOrWhiteSpace(retryResult.DeviceId))
                        {
                            _deviceId = retryResult.DeviceId;
                            return retryResult;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to standard error reporting
            }

            throw CreateRequestFailure(
                "OpenConnection",
                response.StatusCode,
                response.Content,
                includeResponseReason:
                    true,
                serverReason:
                    result?.Error ??
                    result?.OpenResult);
        }


        if (
            result is null ||
            string.IsNullOrWhiteSpace(
                result.DeviceId)
        )
        {
            throw new InvalidDataException(
                "CashDevice OpenConnection did not return a deviceID.");
        }


        _deviceId =
            result.DeviceId;


        return result;
    }


    /*
     * ============================================================
     * Device Status
     * ============================================================
     */

    public async Task<
        IReadOnlyList<DeviceStatusItemResponse>>
        GetDeviceStatusAsync(
            CancellationToken cancellationToken = default)
    {
        using var request =
            CreateAuthorizedDeviceRequest(
                HttpMethod.Get,
                DeviceStatusEndpoint);


        var response =
            await SendAndReadAsync(
                    request,
                    "GetDeviceStatus",
                    cancellationToken)
                .ConfigureAwait(false);


        if (
            !response.IsSuccessStatusCode
        )
        {
            throw CreateRequestFailure(
                "GetDeviceStatus",
                response.StatusCode,
                response.Content,
                includeResponseReason:
                    true);
        }


        if (
            string.IsNullOrWhiteSpace(
                response.Content)
        )
        {
            return Array.Empty<
                DeviceStatusItemResponse>();
        }


        try
        {
            return
                JsonSerializer.Deserialize(
                    response.Content,
                    CashDeviceJsonContext
                        .Default
                        .DeviceStatusItemResponseArray)
                ??
                Array.Empty<
                    DeviceStatusItemResponse>();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "CashDevice GetDeviceStatus returned malformed JSON.",
                exception);
        }
    }


    /*
     * ============================================================
     * Escrow Auto-Accept
     * ============================================================
     *
     * ITL contract:
     *
     * POST
     * /api/CashDevice/SetAutoAccept?deviceID=<id>
     *
     * Content-Type:
     * application/json
     *
     * Body:
     *
     * false
     *
     * or:
     *
     * true
     * ============================================================
     */

    public Task SetAutoAcceptAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return SendBooleanDeviceCommandAsync(
            SetAutoAcceptEndpoint,
            "SetAutoAccept",
            enabled,
            cancellationToken);
    }


    /*
     * ============================================================
     * Enable Acceptor
     * ============================================================
     */

    public Task EnableAcceptorAsync(
        CancellationToken cancellationToken = default)
    {
        return SendDeviceCommandAsync(
            EnableAcceptorEndpoint,
            "EnableAcceptor",
            cancellationToken);
    }


    /*
     * ============================================================
     * Disable Acceptor
     * ============================================================
     */

    public Task DisableAcceptorAsync(
        CancellationToken cancellationToken = default)
    {
        return SendDeviceCommandAsync(
            DisableAcceptorEndpoint,
            "DisableAcceptor",
            cancellationToken);
    }


    /*
     * ============================================================
     * Accept Escrow
     * ============================================================
     */

    public Task AcceptFromEscrowAsync(
        CancellationToken cancellationToken = default)
    {
        return SendDeviceCommandAsync(
            AcceptFromEscrowEndpoint,
            "AcceptFromEscrow",
            cancellationToken);
    }


    /*
     * ============================================================
     * Return Escrow
     * ============================================================
     */

    public Task ReturnFromEscrowAsync(
        CancellationToken cancellationToken = default)
    {
        return SendDeviceCommandAsync(
            ReturnFromEscrowEndpoint,
            "ReturnFromEscrow",
            cancellationToken);
    }


    /*
     * ============================================================
     * Disconnect Device
     * ============================================================
     */

    public async Task DisconnectDeviceAsync(
        CancellationToken cancellationToken = default)
    {
        if (
            !HasOpenDevice
        )
        {
            ClearSession();

            return;
        }


        await SendDeviceCommandAsync(
                DisconnectDeviceEndpoint,
                "DisconnectDevice",
                cancellationToken)
            .ConfigureAwait(false);


        ClearSession();
    }


    /*
     * ============================================================
     * Clear Session
     * ============================================================
     */

    public void ClearSession()
    {
        _deviceId =
            null;

        _bearerToken =
            null;
    }


    /*
     * ============================================================
     * Standard Device POST Command
     * ============================================================
     *
     * Used by endpoints whose request body is empty:
     *
     * EnableAcceptor
     * DisableAcceptor
     * AcceptFromEscrow
     * ReturnFromEscrow
     * DisconnectDevice
     * ============================================================
     */

    private async Task SendDeviceCommandAsync(
        string endpoint,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request =
            CreateAuthorizedDeviceRequest(
                HttpMethod.Post,
                endpoint);


        var response =
            await SendAndReadAsync(
                    request,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);


        if (
            !response.IsSuccessStatusCode
        )
        {
            throw CreateRequestFailure(
                operation,
                response.StatusCode,
                response.Content,
                includeResponseReason:
                    true);
        }
    }


    /*
     * ============================================================
     * Boolean JSON Device Command
     * ============================================================
     *
     * Required by ITL SetAutoAccept.
     *
     * IMPORTANT:
     *
     * This is intentionally not:
     *
     * ?value=false
     *
     * and it is intentionally not form data.
     *
     * ITL expects:
     *
     * Content-Type: application/json
     *
     * false
     *
     * as the complete request body.
     * ============================================================
     */

    private async Task SendBooleanDeviceCommandAsync(
        string endpoint,
        string operation,
        bool value,
        CancellationToken cancellationToken)
    {
        using var request =
            CreateAuthorizedDeviceRequest(
                HttpMethod.Post,
                endpoint);


        /*
         * Explicit StringContent is used here rather than relying on
         * serializer inference because the ITL endpoint expects the
         * raw JSON primitive:
         *
         * false
         *
         * or:
         *
         * true
         */

        request.Content =
            new StringContent(
                value
                    ? "true"
                    : "false",
                Encoding.UTF8,
                "application/json");


        var response =
            await SendAndReadAsync(
                    request,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);


        if (
            !response.IsSuccessStatusCode
        )
        {
            throw CreateRequestFailure(
                operation,
                response.StatusCode,
                response.Content,
                includeResponseReason:
                    true);
        }
    }


    /*
     * ============================================================
     * Authorized Device Request
     * ============================================================
     */

    private HttpRequestMessage
        CreateAuthorizedDeviceRequest(
            HttpMethod method,
            string endpoint)
    {
        var deviceId =
            _deviceId ??
            throw new InvalidOperationException(
                "CashDevice OpenConnection must complete first.");


        var separator =
            endpoint.Contains(
                '?',
                StringComparison.Ordinal)
                ? "&"
                : "?";


        var endpointWithDevice =
            endpoint +
            separator +
            "deviceID=" +
            Uri.EscapeDataString(
                deviceId);


        return CreateAuthorizedRequest(
            method,
            endpointWithDevice);
    }


    /*
     * ============================================================
     * Authorized Request
     * ============================================================
     */

    private HttpRequestMessage
        CreateAuthorizedRequest(
            HttpMethod method,
            string endpoint)
    {
        var token =
            _bearerToken ??
            throw new InvalidOperationException(
                "CashDevice authentication must complete first.");


        var request =
            new HttpRequestMessage(
                method,
                CreateUri(
                    endpoint));


        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);


        return request;
    }


    /*
     * ============================================================
     * URI
     * ============================================================
     */

    private Uri CreateUri(
        string endpoint)
    {
        return new Uri(
            _baseUri,
            endpoint);
    }


    /*
     * ============================================================
     * Send / Read
     * ============================================================
     */

    private async Task<ResponseSnapshot>
        SendAndReadAsync(
            HttpRequestMessage request,
            string operation,
            CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);


        timeout.CancelAfter(
            _options.RequestTimeout);


        try
        {
            using var response =
                await _httpClient
                    .SendAsync(
                        request,
                        HttpCompletionOption
                            .ResponseHeadersRead,
                        timeout.Token)
                    .ConfigureAwait(false);


            var content =
                await response.Content
                    .ReadAsStringAsync(
                        timeout.Token)
                    .ConfigureAwait(false);


            return new ResponseSnapshot(
                response.StatusCode,
                response.IsSuccessStatusCode,
                content);
        }
        catch (OperationCanceledException exception)
            when (
                !cancellationToken
                    .IsCancellationRequested
            )
        {
            throw new TimeoutException(
                $"CashDevice REST operation '{operation}' timed out.",
                exception);
        }
    }


    /*
     * ============================================================
     * Request Failure
     * ============================================================
     */

    private HttpRequestException
        CreateRequestFailure(
            string operation,
            HttpStatusCode statusCode,
            string responseContent,
            bool includeResponseReason,
            string? serverReason = null)
    {
        var reason =
            serverReason;


        if (
            includeResponseReason &&
            string.IsNullOrWhiteSpace(
                reason) &&
            !string.IsNullOrWhiteSpace(
                responseContent)
        )
        {
            try
            {
                var error =
                    JsonSerializer.Deserialize(
                        responseContent,
                        CashDeviceJsonContext
                            .Default
                            .RestErrorResponse);


                reason =
                    error?.Message ??
                    error?.Error ??
                    error?.OpenResult;
            }
            catch (JsonException)
            {
                /*
                 * ITL frequently returns plain-text command responses,
                 * especially for hardware-state errors.
                 */

                reason =
                    responseContent
                        .Trim();
            }
        }


        if (
            !string.IsNullOrWhiteSpace(
                reason)
        )
        {
            reason =
                Redact(
                    reason);


            if (
                reason.Length >
                300
            )
            {
                reason =
                    reason[..300] +
                    "...";
            }
        }


        var message =
            $"CashDevice REST operation '{operation}' failed with " +
            $"HTTP {(int)statusCode} ({statusCode}).";


        if (
            !string.IsNullOrWhiteSpace(
                reason)
        )
        {
            message +=
                " " +
                reason;
        }


        return new HttpRequestException(
            message,
            inner:
                null,
            statusCode);
    }


    /*
     * ============================================================
     * Sensitive Data Redaction
     * ============================================================
     */

    private string Redact(
        string value)
    {
        var redacted =
            value;


        if (
            !string.IsNullOrEmpty(
                _options.Password)
        )
        {
            redacted =
                redacted.Replace(
                    _options.Password,
                    "[REDACTED]",
                    StringComparison.Ordinal);
        }


        if (
            !string.IsNullOrEmpty(
                _bearerToken)
        )
        {
            redacted =
                redacted.Replace(
                    _bearerToken,
                    "[REDACTED]",
                    StringComparison.Ordinal);
        }


        return redacted;
    }


    /*
     * ============================================================
     * Response Snapshot
     * ============================================================
     */

    private sealed record ResponseSnapshot(
        HttpStatusCode StatusCode,
        bool IsSuccessStatusCode,
        string Content);
}