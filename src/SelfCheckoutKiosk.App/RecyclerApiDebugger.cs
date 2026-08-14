using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace SelfCheckoutKiosk.App;

/// <summary>
/// Raw REST API diagnostic tool.
///
/// HOW TO RUN:
///   dotnet run --project src/SelfCheckoutKiosk.App -- --debug-recycler
///
/// PURPOSE:
///   Arms the cash recycler shutter, then continuously polls the vendor REST API
///   and PRINTS the FULL RAW JSON response every time it changes.
///
///   Insert a real banknote while this is running — you will see exactly what
///   JSON fields the vendor server returns so we can fix the event parser.
///
///   The output is also written to:
///     debug_recycler_api.log  (in the project root)
///
/// Press Q to stop and disarm.
/// </summary>
internal static class RecyclerApiDebugger
{
    private const string BaseUrl  = "http://localhost:5000";
    private const string DeviceId = "NOTE_VALIDATOR-COM7";

    // Simple mutable string wrapper so we can pass "last seen" state into async helpers
    private sealed class Seen { public string Last = ""; }

    internal static async Task RunAsync()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     CASH RECYCLER — RAW REST API DIAGNOSTIC MODE              ║");
        Console.WriteLine("║     INSERT A BANKNOTE to see the raw JSON payload             ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        // ── Setup HTTP client ─────────────────────────────────────────────────
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            string apiKey = File.ReadAllText("api_key.secret").Trim();
            var keyBytes = Encoding.UTF8.GetBytes(apiKey);
            var desc = new SecurityTokenDescriptor
            {
                Expires = DateTime.UtcNow.AddHours(24),
                Issuer = "INNOVATIVETECHNOLOGY",
                Audience = "INNOVATIVETECHNOLOGY",
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(keyBytes),
                    SecurityAlgorithms.HmacSha256Signature)
            };
            var handler = new JwtSecurityTokenHandler();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", handler.WriteToken(handler.CreateToken(desc)));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  [AUTH] JWT token loaded from api_key.secret");
            Console.ResetColor();
        }
        catch { /* No key file — run without auth */ }

        // ── Open COM7 connection ──────────────────────────────────────────────
        try
        {
            using var c = new StringContent("{\"comPort\":\"COM7\"}", Encoding.UTF8, "application/json");
            var r = await http.PostAsync($"{BaseUrl}/api/CashDevice/OpenConnection", c);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [OPEN] OpenConnection → {(int)r.StatusCode} {r.ReasonPhrase}");
            Console.ResetColor();
        }
        catch (Exception ex) { Console.WriteLine($"  [OPEN] {ex.Message}"); }

        // ── Arm acceptor ──────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  Arming acceptor (opening shutter)...");
        try
        {
            using var ac = new StringContent("true", Encoding.UTF8, "application/json");
            await http.PostAsync($"{BaseUrl}/api/CashDevice/SetAutoAccept?deviceID={DeviceId}", ac);

            using var ec = new StringContent($"{{\"deviceID\":\"{DeviceId}\"}}", Encoding.UTF8, "application/json");
            var er = await http.PostAsync($"{BaseUrl}/api/CashDevice/EnableAcceptor?deviceID={DeviceId}", ec);
            Console.ForegroundColor = er.IsSuccessStatusCode ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"  [ARM] EnableAcceptor → {(int)er.StatusCode} {er.ReasonPhrase}");
            Console.ResetColor();
        }
        catch (Exception ex) { Console.WriteLine($"  [ARM] {ex.Message}"); }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ════════════════════════════════════════════════════════════");
        Console.WriteLine("  🟢  SHUTTER ARMED — INSERT A BANKNOTE NOW (USD or KHR)");
        Console.WriteLine("  ════════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("  Polling every 250 ms. Raw JSON printed whenever it changes.");
        Console.WriteLine("  Fields highlighted in yellow are the note amount/currency.");
        Console.WriteLine("  Press Q to stop.");
        Console.WriteLine();

        // ── Main poll loop ────────────────────────────────────────────────────
        using var cts = new CancellationTokenSource();
        var keyTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Q)
                { cts.Cancel(); break; }
                Thread.Sleep(100);
            }
        });

        var log   = new List<string>();
        var seen1 = new Seen();
        var seen2 = new Seen();
        var seen3 = new Seen();
        int tick  = 0;

        while (!cts.IsCancellationRequested)
        {
            await Dump(http, $"{BaseUrl}/api/CashDevice/GetDeviceStatus?deviceID={DeviceId}", "GetDeviceStatus", seen1, log, cts.Token);
            await Dump(http, $"{BaseUrl}/api/device/status",                                   "device/status",   seen2, log, cts.Token);
            if (tick % 4 == 0)
                await Dump(http, $"{BaseUrl}/api/CashDevice/GetLastEvent?deviceID={DeviceId}", "GetLastEvent",    seen3, log, cts.Token);
            tick++;

            try { await Task.Delay(250, cts.Token); }
            catch (OperationCanceledException) { break; }
        }

        // ── Disarm ────────────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  Disarming...");
        try
        {
            using var dc = new StringContent($"{{\"deviceID\":\"{DeviceId}\"}}", Encoding.UTF8, "application/json");
            var dr = await http.PostAsync($"{BaseUrl}/api/CashDevice/DisableAcceptor?deviceID={DeviceId}", dc);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"  [DISARM] DisableAcceptor → {(int)dr.StatusCode} {dr.ReasonPhrase}");
            Console.ResetColor();
        }
        catch (Exception ex) { Console.WriteLine($"  [DISARM] {ex.Message}"); }

        // ── Save log ──────────────────────────────────────────────────────────
        try
        {
            string logPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "debug_recycler_api.log"));
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllLines(logPath, log);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n  Log saved → {logPath}");
            Console.ResetColor();
        }
        catch { }

        Console.WriteLine("\n  Done. Share the output above or the log file so we can fix the parser.");
        await keyTask;
    }

    private static async Task Dump(HttpClient http, string url, string label, Seen seen, List<string> log, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync(url, ct);
            string raw = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw) || raw == seen.Last) return;
            seen.Last = raw;

            // Pretty-print using low-level writer (no reflection — AOT-safe)
            string pretty = PrettyPrint(raw);

            string ts     = DateTime.Now.ToString("HH:mm:ss.fff");
            string header = $"[{ts}] [{label}] HTTP {(int)resp.StatusCode} ──── CHANGED ────";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  " + header);
            Console.ResetColor();

            foreach (var line in pretty.Split('\n'))
            {
                bool hi = line.Contains("NOTE",        StringComparison.OrdinalIgnoreCase)
                       || line.Contains("ESCROW",      StringComparison.OrdinalIgnoreCase)
                       || line.Contains("CREDIT",      StringComparison.OrdinalIgnoreCase)
                       || line.Contains("INSERT",      StringComparison.OrdinalIgnoreCase)
                       || line.Contains("STACK",       StringComparison.OrdinalIgnoreCase)
                       || line.Contains("ACCEPT",      StringComparison.OrdinalIgnoreCase)
                       || line.Contains("value",       StringComparison.OrdinalIgnoreCase)
                       || line.Contains("amount",      StringComparison.OrdinalIgnoreCase)
                       || line.Contains("countryCode", StringComparison.OrdinalIgnoreCase)
                       || line.Contains("currency",    StringComparison.OrdinalIgnoreCase)
                       || line.Contains("denomination",StringComparison.OrdinalIgnoreCase)
                       || line.Contains("isoCode",     StringComparison.OrdinalIgnoreCase);

                if (hi) Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("    " + line.TrimEnd());
                if (hi) Console.ResetColor();
            }
            Console.WriteLine();

            log.Add(header);
            log.Add(pretty);
            log.Add("");
        }
        catch (OperationCanceledException) { }
        catch { /* endpoint not available — skip silently */ }
    }

    // Manually pretty-print JSON without generic JsonSerializer (AOT-safe)
    private static string PrettyPrint(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var ms  = new System.IO.MemoryStream();
            using var w   = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
            doc.RootElement.WriteTo(w);
            w.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch { return json; }
    }
}
