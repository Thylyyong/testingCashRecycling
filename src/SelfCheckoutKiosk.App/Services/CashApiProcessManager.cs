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

            string? exePath = LocateCashApiExecutable();
            if (!string.IsNullOrEmpty(exePath))
            {
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
                        Console.WriteLine($"[CashApiProcessManager] Started Cash API process: {exePath} (PID: {_apiProcess.Id}, Background: {runInBackground})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CashApiProcessManager] Failed to launch executable '{exePath}': {ex.Message}");
                }
            }
            else
            {
                // Try dotnet run on CashDeviceSimulator project if available
                string? projectPath = LocateSimulatorProject();
                if (!string.IsNullOrEmpty(projectPath))
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
                            Console.WriteLine($"[CashApiProcessManager] Started CashDeviceSimulator (PID: {_apiProcess.Id}, Background: {runInBackground})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CashApiProcessManager] Failed to launch simulator project: {ex.Message}");
                    }
                }
            }
        }

        // Wait for process to become responsive (cold-start can take up to 15s)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        while (!linkedCts.Token.IsCancellationRequested)
        {
            foreach (var url in candidateUrls)
            {
                if (await IsEndpointResponsiveAsync(url, linkedCts.Token))
                {
                    Console.WriteLine($"[CashApiProcessManager] Cash API is now online and reachable at {url}");
                    return url;
                }
            }

            try
            {
                await Task.Delay(200, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
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

            // Fast TCP pre-check prevents HttpClient from timing out and spamming TaskCanceledException in debugger
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync(uri.Host, uri.Port);
            var timeoutTask = Task.Delay(150, ct);
            var finished = await Task.WhenAny(connectTask, timeoutTask);

            if (finished != connectTask || !tcp.Connected)
                return false;

            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) };
            // Use root URL for health check — it always returns 200 on both the
            // real ITL API and the simulator, without requiring authentication.
            using var resp = await client.GetAsync($"{baseUrl}/", HttpCompletionOption.ResponseHeadersRead, ct);
            return (int)resp.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static string? LocateCashApiExecutable()
    {
        // 1. Environment variable override
        string? envPath = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_EXE_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // 2. Common directory candidates for real ITL SDK CashDevice-RestAPI.exe
        string baseDir = AppContext.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] potentialDirs =
        {
            Path.Combine(userProfile, "Desktop", "CA", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0 1", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
            Path.Combine(userProfile, "Desktop", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
            Path.Combine(userProfile, "Downloads", "CashDevice-REST-API-V1.6.1-RC.4-Net8.0"),
            baseDir,
            currentDir,
            @"C:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0",
            @"F:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0",
            @"D:\ITL device\ITL sdk package\CashDevice-REST-API-V1.6.1-RC.4-Net8.0",
            Path.Combine(currentDir, "tools", "CashDeviceSimulator", "bin", "Debug", "net10.0"),
            Path.Combine(currentDir, "tools", "CashDeviceSimulator", "bin", "Release", "net10.0"),
            Path.Combine(baseDir, "..", "..", "..", "..", "tools", "CashDeviceSimulator", "bin", "Debug", "net10.0"),
            Path.Combine(baseDir, "..", "..", "..", "..", "tools", "CashDeviceSimulator", "bin", "Release", "net10.0")
        };

        // Check for real ITL hardware server executable first
        foreach (var dir in potentialDirs)
        {
            if (Directory.Exists(dir))
            {
                string realExe = Path.Combine(dir, "CashDevice-RestAPI.exe");
                if (File.Exists(realExe))
                {
                    return realExe;
                }
            }
        }

        // Fallback to simulator executable if real ITL SDK is not installed
        foreach (var dir in potentialDirs)
        {
            if (Directory.Exists(dir))
            {
                string simExe = Path.Combine(dir, "CashDeviceSimulator.exe");
                if (File.Exists(simExe))
                {
                    return simExe;
                }
            }
        }

        return null;
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
