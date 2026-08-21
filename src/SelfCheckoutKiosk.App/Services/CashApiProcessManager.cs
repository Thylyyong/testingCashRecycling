using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Manages the background lifecycle of the Cash Device REST API / Simulator process.
/// Automatically starts the API when the kiosk application launches if it is not already running.
/// </summary>
public static class CashApiProcessManager
{
    private static Process? _apiProcess;
    private static readonly object _lock = new();

    /// <summary>
    /// Ensures that the Cash REST API / Simulator process is running and responding to HTTP requests.
    /// Returns the active base URL (e.g. "http://localhost:5000" or "http://localhost:5055").
    /// </summary>
    public static async Task<string> EnsureCashApiRunningAsync(CancellationToken cancellationToken = default)
    {
        string? envUrl = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_BASE_URL");
        string[] candidateUrls = !string.IsNullOrWhiteSpace(envUrl)
            ? new[] { envUrl.TrimEnd('/') }
            : new[] { "http://127.0.0.1:5000", "http://localhost:5000", "http://localhost:5055", "http://127.0.0.1:5055" };

        // 1. Check if already responding on any candidate URL
        foreach (var url in candidateUrls)
        {
            if (await IsEndpointResponsiveAsync(url, cancellationToken))
            {
                Console.WriteLine($"[CashApiProcessManager] Cash API is already running on {url}");
                return url;
            }
        }

        // 2. Not running — launch a fresh instance
        return await LaunchCashApiProcessAsync(cancellationToken);
    }

    /// <summary>
    /// Forcefully terminates any running Cash API / Simulator processes and launches a fresh one.
    /// Used to self-heal when a previous debug session left the serial port locked.
    /// </summary>
    public static async Task<string> RestartCashApiAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[CashApiProcessManager] Restarting Cash API to clear any stale device connections...");
        lock (_lock)
        {
            KillProcessSafely(_apiProcess);
            _apiProcess = null;
        }

        KillExistingCashApiProcesses();

        try
        {
            await Task.Delay(1000, cancellationToken);
        }
        catch { }

        return await LaunchCashApiProcessAsync(cancellationToken);
    }

    public static void KillExistingCashApiProcesses()
    {
        string[] processNames = { "CashDevice-RestAPI", "CashDeviceSimulator" };
        foreach (var name in processNames)
        {
            try
            {
                var running = Process.GetProcessesByName(name);
                foreach (var p in running)
                {
                    try
                    {
                        Console.WriteLine($"[CashApiProcessManager] Terminating orphaned process {p.ProcessName} (PID: {p.Id})...");
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(2000);
                        p.Dispose();
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Controls whether the Cash API / Simulator process runs silently in the background (hidden window)
    /// or in a visible console window for debugging.
    /// Defaults to TRUE (hidden background execution).
    /// Can be toggled in code (CashApiProcessManager.RunInBackground = false) or via environment variable
    /// 'SELFCHECKOUTKIOSK_SHOW_CASH_API_WINDOW=1' or 'true'.
    /// </summary>
    public static bool RunInBackground { get; set; } = true;

    private static bool ShouldRunInBackground()
    {
        string? showEnv = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_SHOW_CASH_API_WINDOW");
        if (!string.IsNullOrWhiteSpace(showEnv) && (showEnv == "1" || showEnv.Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
            return false; // User requested visible window for debugging
        }
        return RunInBackground;
    }

    private static async Task<string> LaunchCashApiProcessAsync(CancellationToken cancellationToken = default)
    {
        string? envUrl = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_BASE_URL");
        string[] candidateUrls = !string.IsNullOrWhiteSpace(envUrl)
            ? new[] { envUrl.TrimEnd('/') }
            : new[] { "http://127.0.0.1:5000", "http://localhost:5000", "http://localhost:5055", "http://127.0.0.1:5055" };

        bool runInBackground = ShouldRunInBackground();

        lock (_lock)
        {
            if (_apiProcess != null && !_apiProcess.HasExited)
            {
                return candidateUrls[0];
            }
        }

        var candidates = GetCashApiExecutableCandidates();

        foreach (var exePath in candidates)
        {
            lock (_lock)
            {
                KillProcessSafely(_apiProcess);
                _apiProcess = null;

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                        UseShellExecute = !runInBackground,
                        CreateNoWindow = runInBackground,
                        WindowStyle = runInBackground ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                    };

                    _apiProcess = Process.Start(startInfo);
                    if (_apiProcess != null)
                    {
                        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillProcessSafely(_apiProcess);
                        Console.WriteLine($"[CashApiProcessManager] Attempting to launch Cash API: {exePath} (PID: {_apiProcess.Id}, Background: {runInBackground})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CashApiProcessManager] Failed to launch executable '{exePath}': {ex.Message}");
                    _apiProcess = null;
                }
            }

            if (_apiProcess == null) continue;

            // Wait up to 12s for this candidate process to finish booting and bind to port
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            while (!linkedCts.Token.IsCancellationRequested)
            {
                if (_apiProcess == null || _apiProcess.HasExited)
                {
                    Console.WriteLine($"[CashApiProcessManager] Executable '{exePath}' exited prematurely (exit code: {_apiProcess?.ExitCode}). Trying next candidate...");
                    break;
                }

                foreach (var url in candidateUrls)
                {
                    if (await IsEndpointResponsiveAsync(url, linkedCts.Token))
                    {
                        Console.WriteLine($"[CashApiProcessManager] Cash API is now online and reachable at {url} (via {Path.GetFileName(exePath)})");
                        return url;
                    }
                }

                try
                {
                    await Task.Delay(250, linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            // If not responsive or exited, kill it and continue to next candidate
            lock (_lock)
            {
                KillProcessSafely(_apiProcess);
                _apiProcess = null;
            }
        }

        // Fallback: Try dotnet run on simulator project if available (development machine)
        string? projectPath = LocateSimulatorProject();
        if (!string.IsNullOrEmpty(projectPath))
        {
            lock (_lock)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{projectPath}\"",
                        WorkingDirectory = Path.GetDirectoryName(projectPath) ?? AppContext.BaseDirectory,
                        UseShellExecute = !runInBackground,
                        CreateNoWindow = runInBackground,
                        WindowStyle = runInBackground ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                    };

                    _apiProcess = Process.Start(startInfo);
                    if (_apiProcess != null)
                    {
                        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillProcessSafely(_apiProcess);
                        Console.WriteLine($"[CashApiProcessManager] Started CashDeviceSimulator via dotnet run (PID: {_apiProcess.Id})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CashApiProcessManager] Failed to launch simulator project: {ex.Message}");
                }
            }

            if (_apiProcess != null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                while (!linkedCts.Token.IsCancellationRequested)
                {
                    if (_apiProcess == null || _apiProcess.HasExited) break;

                    foreach (var url in candidateUrls)
                    {
                        if (await IsEndpointResponsiveAsync(url, linkedCts.Token))
                        {
                            Console.WriteLine($"[CashApiProcessManager] Cash API is now online and reachable at {url}");
                            return url;
                        }
                    }

                    try { await Task.Delay(250, linkedCts.Token); } catch { break; }
                }
            }
        }

        return candidateUrls[0];
    }

    private static async Task<bool> IsEndpointResponsiveAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                return false;

            // Fast TCP pre-check
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync(uri.Host, uri.Port);
            var timeoutTask = Task.Delay(500, ct);
            var finished = await Task.WhenAny(connectTask, timeoutTask);

            if (finished != connectTask || !tcp.Connected)
                return false;

            // If TCP connected, verify HTTP response
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };

            string[] probeEndpoints = { "/", "/swagger/index.html", "/api/health", "/api/cashdevice/status" };
            foreach (var ep in probeEndpoints)
            {
                try
                {
                    using var resp = await client.GetAsync($"{baseUrl.TrimEnd('/')}{ep}", HttpCompletionOption.ResponseHeadersRead, ct);
                    if ((int)resp.StatusCode < 500)
                    {
                        return true;
                    }
                }
                catch { }
            }

            // If TCP port is open and listening, consider it responsive
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> GetCashApiExecutableCandidates()
    {
        var candidates = new List<string>();

        // 1. Environment variable override
        string? envPath = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_EXE_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            candidates.Add(Path.GetFullPath(envPath));
        }

        string baseDir = AppContext.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] searchDirs =
        {
            baseDir,
            Path.Combine(baseDir, "CashAPI"),
            Path.Combine(baseDir, "CashAPI", "Simulator"),
            Path.Combine(baseDir, "..", "CashAPI"),
            Path.Combine(baseDir, "..", "CashAPI", "Simulator"),
            Path.Combine(baseDir, ".."),
            Path.Combine(baseDir, "..", "CashDevice-RestAPI"),
            Path.Combine(baseDir, "CashDevice-RestAPI"),
            Path.Combine(baseDir, "..", "CashDeviceSimulator-API"),
            Path.Combine(baseDir, "CashDeviceSimulator-API"),
            currentDir,
            Path.Combine(currentDir, "CashAPI"),
            Path.Combine(currentDir, "CashAPI", "Simulator"),
            Path.Combine(currentDir, "CashDevice-RestAPI"),
            Path.Combine(currentDir, "CashDeviceSimulator-API"),
            Path.Combine(currentDir, "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
            Path.Combine(baseDir, "..", "..", "..", "..", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
            Path.Combine(userProfile, "Desktop", "CA", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0 1", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
            Path.Combine(userProfile, "Desktop", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
            Path.Combine(userProfile, "Downloads", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
            @"C:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0",
            @"F:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0",
            @"D:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0",
            Path.Combine(currentDir, "tools", "CashDeviceSimulator", "bin", "Release", "net10.0", "win-x64"),
            Path.Combine(currentDir, "tools", "CashDeviceSimulator", "bin", "Debug", "net10.0"),
            Path.Combine(baseDir, "..", "..", "..", "..", "tools", "CashDeviceSimulator", "bin", "Release", "net10.0", "win-x64"),
            Path.Combine(baseDir, "..", "..", "..", "..", "tools", "CashDeviceSimulator", "bin", "Debug", "net10.0"),
            Path.Combine(baseDir, "..", "..", "..", "..", "tools", "CashDeviceSimulator", "bin", "Release", "net10.0")
        };

        // PASS 1: Real ITL CashDevice-RestAPI.exe (Physical Hardware)
        foreach (var dir in searchDirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                string realExe = Path.Combine(dir, "CashDevice-RestAPI.exe");
                if (File.Exists(realExe))
                {
                    string full = Path.GetFullPath(realExe);
                    if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(full);
                }
            }
            catch { }
        }

        // PASS 2: Self-contained CashDeviceSimulator.exe (Guaranteed to run on any machine without runtime dependencies)
        foreach (var dir in searchDirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                string simExe = Path.Combine(dir, "CashDeviceSimulator.exe");
                if (File.Exists(simExe))
                {
                    string full = Path.GetFullPath(simExe);
                    if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(full);
                }
            }
            catch { }
        }

        return candidates;
    }

    private static string? LocateSimulatorProject()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string baseDir = AppContext.BaseDirectory;

        string[] candidateProjectPaths =
        {
            Path.Combine(currentDir, "tools", "CashDeviceSimulator", "CashDeviceSimulator.csproj"),
            Path.Combine(baseDir, "..", "..", "..", "..", "tools", "CashDeviceSimulator", "CashDeviceSimulator.csproj"),
            Path.Combine(baseDir, "..", "..", "..", "tools", "CashDeviceSimulator", "CashDeviceSimulator.csproj")
        };

        foreach (var path in candidateProjectPaths)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch { }
        }

        return null;
    }

    private static void KillProcessSafely(Process? process)
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.Dispose();
            }
        }
        catch { }
    }

    public static void Shutdown()
    {
        lock (_lock)
        {
            KillProcessSafely(_apiProcess);
            _apiProcess = null;
        }
    }
}
