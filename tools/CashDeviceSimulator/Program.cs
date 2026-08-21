using System.Text.Json.Serialization;
using SelfCheckoutKiosk.Tools.CashDeviceSimulator;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
});

int targetPort = 5000;
if (args.Length > 0 && int.TryParse(args[0], out int customPort))
{
    targetPort = customPort;
}
else if (IsPortInUse(5000))
{
    targetPort = 5055;
    Console.WriteLine($"[CashDeviceSimulator] Port 5000 is already in use, binding to http://localhost:{targetPort} ...");
}

builder.WebHost.UseUrls($"http://127.0.0.1:{targetPort}", $"http://localhost:{targetPort}");

var app = builder.Build();
var state = new DeviceState();

// Global simulated hardware state
string activeDeviceId = "NOTE_VALIDATOR-COM6";
string simulatedState = "IDLE";
int simulatedRawValue = 0;
string simulatedCurrency = "USD";
bool isAcceptorEnabled = false;

// ---------------------------------------------------------------------------
// 1. Authentication (api/Users/Authenticate)
// ---------------------------------------------------------------------------
app.MapPost("/api/Users/Authenticate", (AuthenticateDto request, ILogger<Program> log) =>
{
    log.LogInformation("🔐 [AUTHENTICATE] User '{Username}' attempting login", request.Username);

    // Accept admin/password and operator/operator (or any valid test credential)
    bool isValid = (!string.IsNullOrWhiteSpace(request.Username) && !string.IsNullOrWhiteSpace(request.Password));
    if (!isValid)
    {
        log.LogWarning("❌ [AUTHENTICATE] Authentication failed for '{Username}'", request.Username);
        return Results.Json(new { message = "Invalid username or password" }, statusCode: 400);
    }

    log.LogInformation("✅ [AUTHENTICATE] User '{Username}' authenticated successfully! Token generated.", request.Username);
    return Results.Json(new
    {
        token = "simulator-bearer-token-" + Guid.NewGuid().ToString("N"),
        username = request.Username,
        id = 1
    });
});

// ---------------------------------------------------------------------------
// 2. Open Connection (api/CashDevice/OpenConnection)
// ---------------------------------------------------------------------------
app.MapPost("/api/CashDevice/OpenConnection", (OpenConnDto? request, ILogger<Program> log) =>
{
    string comPort = request?.ComPort ?? "COM5";
    activeDeviceId = $"NOTE_VALIDATOR-{comPort}";
    log.LogInformation("🔌 [DEVICE CONNECTED] Cash Device successfully opened on port {ComPort} (DeviceID: {DeviceId})", comPort, activeDeviceId);

    return Results.Json(new
    {
        deviceID = activeDeviceId,
        isOpen = true,
        deviceModel = "NV200-Spectra",
        sspProtocolVersion = 6,
        firmware = "v1.6.1-RC.4",
        dataset = "USD,KHR",
        openResult = "OK",
        acceptorEnabled = false,
        autoAcceptEscrowEnabled = false,
        payoutEnabled = false
    });
});

// ---------------------------------------------------------------------------
// 3. Get Device Status (api/CashDevice/GetDeviceStatus)
// ---------------------------------------------------------------------------
app.MapGet("/api/CashDevice/GetDeviceStatus", (HttpContext context, ILogger<Program> log) =>
{
    string queryDeviceId = context.Request.Query["deviceID"].ToString();
    string effectiveDeviceId = string.IsNullOrWhiteSpace(queryDeviceId) ? activeDeviceId : queryDeviceId;

    string currentState = simulatedState;
    int currentValue = simulatedRawValue;
    string currentCurrency = simulatedCurrency;

    // Reset state after delivering note event to simulate event consumption
    if (simulatedState != "IDLE")
    {
        simulatedState = "IDLE";
        simulatedRawValue = 0;
    }

    var deviceStatuses = new[]
    {
        new
        {
            type = "NOTE_VALIDATOR",
            deviceID = effectiveDeviceId,
            stateAsString = currentState,
            isRunning = isAcceptorEnabled,
            eventTypeAsString = currentState,
            value = currentValue,
            value2 = 0,
            countryCode = currentCurrency,
            message = "OK",
            isOnline = true
        }
    };

    return Results.Json(deviceStatuses);
});

// ---------------------------------------------------------------------------
// 4. Acceptor Arm / Disarm & Inhibit
// ---------------------------------------------------------------------------
app.MapPost("/api/CashDevice/EnableAcceptor", (HttpContext context, ILogger<Program> log) =>
{
    isAcceptorEnabled = true;
    log.LogInformation("🟢 [HARDWARE ARMED] CashDevice/EnableAcceptor - Green LED ON, Note Slot Ready on {DeviceId}", activeDeviceId);
    return Results.Json(new { success = true, result = "OK" });
});

app.MapPost("/api/CashDevice/DisableAcceptor", (HttpContext context, ILogger<Program> log) =>
{
    isAcceptorEnabled = false;
    log.LogInformation("🔴 [HARDWARE DISARMED] CashDevice/DisableAcceptor - Green LED OFF, Slot Inhibited on {DeviceId}", activeDeviceId);
    return Results.Json(new { success = true, result = "OK" });
});

app.MapPost("/api/CashDevice/SetAutoAccept", (HttpContext context, ILogger<Program> log) =>
{
    string autoAccept = context.Request.Query["autoAccept"].ToString();
    log.LogInformation("⚙️ [CONFIG] CashDevice/SetAutoAccept autoAccept={AutoAccept} on {DeviceId}", autoAccept, activeDeviceId);
    return Results.Json(new { success = true, result = "OK" });
});

app.MapPost("/api/CashDevice/UnInhibit", (HttpContext context, ILogger<Program> log) =>
{
    log.LogInformation("🟢 [UN-INHIBIT] Channels Uninhibited on {DeviceId}", activeDeviceId);
    return Results.Json(new { success = true, result = "OK" });
});

app.MapPost("/api/CashDevice/Inhibit", (HttpContext context, ILogger<Program> log) =>
{
    log.LogInformation("🔴 [INHIBIT] Channels Inhibited on {DeviceId}", activeDeviceId);
    return Results.Json(new { success = true, result = "OK" });
});

// ---------------------------------------------------------------------------
// 5. Escrow Resolution (Accept / Return)
// ---------------------------------------------------------------------------
app.MapPost("/api/CashDevice/AcceptFromEscrow", (HttpContext context, ILogger<Program> log) =>
{
    log.LogInformation("📥 [ESCROW ACCEPT] Banknote committed to cash vault on {DeviceId}", activeDeviceId);
    simulatedState = "NOTE_STACKED";
    return Results.Json(new { success = true, result = "OK" });
});

app.MapPost("/api/CashDevice/ReturnFromEscrow", (HttpContext context, ILogger<Program> log) =>
{
    log.LogInformation("📤 [ESCROW REJECT] Banknote returned to customer from {DeviceId}", activeDeviceId);
    simulatedState = "NOTE_REJECTED";
    return Results.Json(new { success = true, result = "OK" });
});

// ---------------------------------------------------------------------------
// 6. Disconnect Device
// ---------------------------------------------------------------------------
app.MapPost("/api/CashDevice/DisconnectDevice", (HttpContext context, ILogger<Program> log) =>
{
    log.LogInformation("🔌 [DEVICE DISCONNECTED] Cash Device disconnected on {DeviceId}", activeDeviceId);
    return Results.Json(new { success = true, result = "OK" });
});

// ---------------------------------------------------------------------------
// 7. Legacy Stand-in Endpoints (ItlRestCashRecycler Compatibility)
// ---------------------------------------------------------------------------
app.MapPost("/api/device/initialize", (ILogger<Program> log) =>
{
    log.LogInformation("initialize");
    return Results.Ok();
});

app.MapGet("/api/device/status", (ILogger<Program> log) =>
{
    DeviceStatusDto dto = state.ToStatusDto();
    return Results.Json(dto, SimulatorJsonContext.Default.DeviceStatusDto);
});

app.MapGet("/api/device/cassettes", (ILogger<Program> log) =>
{
    return Results.Json(state.ToCassettesDto(), SimulatorJsonContext.Default.CassetteLevelsDto);
});

app.MapPost("/api/device/accept/enable", (ILogger<Program> log) =>
{
    log.LogInformation("accept/enable (armed)");
    return Results.Ok();
});

app.MapPost("/api/device/accept/disable", (ILogger<Program> log) =>
{
    log.LogInformation("accept/disable (disarmed)");
    return Results.Ok();
});

app.MapPost("/api/device/accept/reject", (ILogger<Program> log) =>
{
    state.EscrowOpen = false;
    log.LogInformation("accept/reject");
    return Results.Ok();
});

app.MapPost("/api/device/dispense", (DispenseRequestDto? request, ILogger<Program> log) =>
{
    var response = new DispenseResponseDto(true, request?.UsdNotes ?? new List<DispenseNoteDto>(), request?.KhrNotes ?? new List<DispenseNoteDto>());
    return Results.Json(response, SimulatorJsonContext.Default.DispenseResponseDto);
});

// ---------------------------------------------------------------------------
// 8. Interactive HTML Web UI & Simulation Trigger Endpoints
// ---------------------------------------------------------------------------
app.MapGet("/api/simulator/insert", (int value, string? currency, ILogger<Program> log) =>
{
    string curr = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant();

    // ITL Raw dataset reports denominations scaled by 100:
    // USD $1 -> 100, USD $5 -> 500
    // KHR 100 -> 10000, KHR 10,000 -> 1000000
    int rawValue = curr == "USD" ? value * 100 : value * 100;

    simulatedRawValue = rawValue;
    simulatedCurrency = curr;
    simulatedState = "NOTE_ESCROW";

    log.LogInformation("💵 [SIMULATED INSERTION] Note: {Value} {Currency} (Raw ITL Value: {RawValue}) -> State: NOTE_ESCROW", value, curr, rawValue);
    return Results.Ok(new { success = true, value, currency = curr, rawValue, state = "NOTE_ESCROW" });
});

app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <title>Cash Device Simulator</title>
    <style>
        body { font-family: system-ui, -apple-system, sans-serif; background: #0f172a; color: #f8fafc; padding: 30px; }
        h1 { color: #38bdf8; margin-bottom: 4px; }
        .meta { color: #94a3b8; font-size: 14px; margin-bottom: 20px; }
        .section { background: #1e293b; border-radius: 12px; padding: 20px; margin-bottom: 20px; border: 1px solid #334155; }
        .btn-group { display: flex; gap: 10px; flex-wrap: wrap; margin-top: 10px; }
        button { background: #0284c7; color: white; border: none; padding: 12px 20px; border-radius: 8px; font-weight: bold; cursor: pointer; font-size: 16px; transition: transform 0.1s, background 0.2s; }
        button:hover { background: #0369a1; transform: scale(1.05); }
        button:active { transform: scale(0.95); }
        .khr-btn { background: #059669; }
        .khr-btn:hover { background: #047857; }
        #status { font-weight: bold; color: #4ade80; margin-top: 15px; min-height: 24px; }
        .routes { font-family: monospace; font-size: 13px; color: #cbd5e1; background: #0f172a; padding: 12px; border-radius: 8px; }
    </style>
</head>
<body>
    <h1>💵 Cash Device Hardware Simulator</h1>
    <div class='meta'>Port: <strong>5000</strong> | Default Credentials: <code>admin</code> / <code>password</code> | Ready for SelfCheckoutKiosk</div>

    <div class='section'>
        <h3>Insert USD Banknote ($)</h3>
        <div class='btn-group'>
            <button onclick='insertNote(1, ""USD"")'>$1</button>
            <button onclick='insertNote(5, ""USD"")'>$5</button>
            <button onclick='insertNote(10, ""USD"")'>$10</button>
            <button onclick='insertNote(20, ""USD"")'>$20</button>
            <button onclick='insertNote(50, ""USD"")'>$50</button>
            <button onclick='insertNote(100, ""USD"")'>$100</button>
        </div>
    </div>

    <div class='section'>
        <h3>Insert KHR Banknote (៛)</h3>
        <div class='btn-group'>
            <button class='khr-btn' onclick='insertNote(100, ""KHR"")'>៛100</button>
            <button class='khr-btn' onclick='insertNote(500, ""KHR"")'>៛500</button>
            <button class='khr-btn' onclick='insertNote(1000, ""KHR"")'>៛1,000</button>
            <button class='khr-btn' onclick='insertNote(2000, ""KHR"")'>៛2,000</button>
            <button class='khr-btn' onclick='insertNote(5000, ""KHR"")'>៛5,000</button>
            <button class='khr-btn' onclick='insertNote(10000, ""KHR"")'>៛10,000</button>
            <button class='khr-btn' onclick='insertNote(20000, ""KHR"")'>៛20,000</button>
            <button class='khr-btn' onclick='insertNote(50000, ""KHR"")'>៛50,000</button>
            <button class='khr-btn' onclick='insertNote(100000, ""KHR"")'>៛100,000</button>
        </div>
    </div>

    <div id='status'></div>

    <script>
        function insertNote(val, curr) {
            fetch(`/api/simulator/insert?value=${val}&currency=${curr}`)
                .then(r => r.json())
                .then(d => {
                    document.getElementById('status').innerText = `✓ Simulated Note: ${curr === 'USD' ? '$' + val : val + ' ៛'} (raw ${d.rawValue}) sent to Kiosk!`;
                });
        }
    </script>
</body>
</html>
", "text/html"));

// ---------------------------------------------------------------------------
// Startup Banner & Route Printing
// ---------------------------------------------------------------------------
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("================================================================================");
Console.WriteLine(" 💵 CashDevice-RestAPI Simulator Online & Listening on: http://localhost:5000/");
Console.WriteLine(" 🔑 Credentials: Username: admin | Password: password (or operator / operator)");
Console.WriteLine("================================================================================");
Console.ResetColor();

Console.WriteLine("📍 Registered API Routes:");
Console.WriteLine("   [POST] /api/Users/Authenticate          -> Authenticate operator/admin");
Console.WriteLine("   [POST] /api/CashDevice/OpenConnection   -> Open COM port / connect device");
Console.WriteLine("   [GET]  /api/CashDevice/GetDeviceStatus  -> Poll device state & banknote stream");
Console.WriteLine("   [POST] /api/CashDevice/EnableAcceptor   -> Arm note validator");
Console.WriteLine("   [POST] /api/CashDevice/DisableAcceptor  -> Disarm note validator");
Console.WriteLine("   [POST] /api/CashDevice/SetAutoAccept    -> Toggle auto-accept in escrow");
Console.WriteLine("   [POST] /api/CashDevice/AcceptFromEscrow -> Commit note to vault");
Console.WriteLine("   [POST] /api/CashDevice/ReturnFromEscrow -> Return note to customer");
Console.WriteLine("   [POST] /api/CashDevice/DisconnectDevice -> Close hardware connection");
Console.WriteLine("   [GET]  /api/simulator/insert            -> Web UI trigger for banknotes");
Console.WriteLine("   [GET]  /                                -> Interactive Web Simulator UI");
Console.WriteLine("================================================================================");
Console.WriteLine("Ready for SelfCheckoutKiosk connection.\n");

app.Run();

static bool IsPortInUse(int port)
{
    try
    {
        using var tcpListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
        tcpListener.Start();
        tcpListener.Stop();
        return false;
    }
    catch
    {
        return true;
    }
}

namespace SelfCheckoutKiosk.Tools.CashDeviceSimulator
{
    internal sealed record AuthenticateDto(
        [property: JsonPropertyName("Username")] string? Username,
        [property: JsonPropertyName("Password")] string? Password);

    internal sealed record OpenConnDto(
        [property: JsonPropertyName("ComPort")] string? ComPort,
        [property: JsonPropertyName("SspAddress")] int SspAddress);

    internal sealed class DeviceState
    {
        public bool EscrowOpen { get; set; }

        public DeviceStatusDto ToStatusDto() => new(
            State: "Idle",
            EscrowedNote: null,
            FaultMessage: null,
            SequenceId: 1);

        public CassetteLevelsDto ToCassettesDto() => new(
        [
            new CassetteLevelDto(500, 50),
            new CassetteLevelDto(1000, 50),
            new CassetteLevelDto(2000, 50),
            new CassetteLevelDto(5000, 50),
            new CassetteLevelDto(10000, 50),
            new CassetteLevelDto(20000, 50),
            new CassetteLevelDto(50000, 50),
            new CassetteLevelDto(100000, 50),
        ]);
    }

    internal sealed record NoteDto(
        [property: JsonPropertyName("currencyCode")] string CurrencyCode,
        [property: JsonPropertyName("denomination")] decimal Denomination);

    internal sealed record DeviceStatusDto(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("escrowedNote")] NoteDto? EscrowedNote,
        [property: JsonPropertyName("faultMessage")] string? FaultMessage,
        [property: JsonPropertyName("sequenceId")] long SequenceId);

    internal sealed record CassetteLevelDto(
        [property: JsonPropertyName("denominationKhr")] int DenominationKhr,
        [property: JsonPropertyName("count")] int Count);

    internal sealed record CassetteLevelsDto(
        [property: JsonPropertyName("levels")] IReadOnlyList<CassetteLevelDto> Levels);

    internal sealed record DispenseNoteDto(
        [property: JsonPropertyName("denomination")] int Denomination,
        [property: JsonPropertyName("count")] int Count);

    internal sealed record DispenseRequestDto(
        [property: JsonPropertyName("usdNotes")] IReadOnlyList<DispenseNoteDto> UsdNotes,
        [property: JsonPropertyName("khrNotes")] IReadOnlyList<DispenseNoteDto> KhrNotes);

    internal sealed record DispenseResponseDto(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("dispensedUsdNotes")] IReadOnlyList<DispenseNoteDto> DispensedUsdNotes,
        [property: JsonPropertyName("dispensedKhrNotes")] IReadOnlyList<DispenseNoteDto> DispensedKhrNotes);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DeviceStatusDto))]
    [JsonSerializable(typeof(CassetteLevelsDto))]
    [JsonSerializable(typeof(DispenseRequestDto))]
    [JsonSerializable(typeof(DispenseResponseDto))]
    internal sealed partial class SimulatorJsonContext : JsonSerializerContext;
}
