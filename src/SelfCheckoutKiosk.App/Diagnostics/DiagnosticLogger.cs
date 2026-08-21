using System;
using System.Diagnostics;
using System.IO;

namespace SelfCheckoutKiosk.App.Diagnostics;

/// <summary>
/// Thread-safe central diagnostic logger that outputs simultaneously to:
/// 1. The visible DiagnosticConsole window (Console.Out)
/// 2. The Visual Studio Debug output window (Debug.WriteLine)
/// 3. Persistent log file in the /logs directory for post-mortem debugging.
/// </summary>
public static class DiagnosticLogger
{
    private static readonly object _syncLock = new();
    private static readonly string _rootLogFilePath;
    private static readonly string _logsSubdirFilePath;

    static DiagnosticLogger()
    {
        _rootLogFilePath = Path.Combine(AppContext.BaseDirectory, "startup_debug.log");
        string logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
        _logsSubdirFilePath = Path.Combine(logsDir, "hardware_debug.log");

        try
        {
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            string header =
                $"\n======================================================\n" +
                $" SESSION STARTED: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                $" OS: {Environment.OSVersion} (64-bit: {Environment.Is64BitOperatingSystem})\n" +
                $" Machine: {Environment.MachineName} | User: {Environment.UserName}\n" +
                $" BaseDirectory: {AppContext.BaseDirectory}\n" +
                $" .NET Runtime: {Environment.Version}\n" +
                $"======================================================\n";

            WriteToFile(header);
        }
        catch { }
    }

    public static string LogFilePath => _rootLogFilePath;

    public static void Log(string message)
    {
        string timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

        Console.WriteLine(timestamped);
        Debug.WriteLine(timestamped);

        WriteToFile(timestamped);
    }

    public static void LogError(string message, Exception? ex = null)
    {
        string text = ex != null
            ? $"[ERROR] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message} Exception: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}"
            : $"[ERROR] [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(text);
            Console.ResetColor();
        }
        catch
        {
            Console.WriteLine(text);
        }

        Debug.WriteLine(text);
        WriteToFile(text);

        // Also append immediately to startup_crash.log on root if it's an error
        try
        {
            string crashLog = Path.Combine(AppContext.BaseDirectory, "startup_crash.log");
            File.AppendAllText(crashLog, text + Environment.NewLine + Environment.NewLine);
        }
        catch { }
    }

    private static void WriteToFile(string text)
    {
        lock (_syncLock)
        {
            try
            {
                File.AppendAllText(_rootLogFilePath, text + Environment.NewLine);
            }
            catch { }

            try
            {
                File.AppendAllText(_logsSubdirFilePath, text + Environment.NewLine);
            }
            catch { }
        }
    }
}
