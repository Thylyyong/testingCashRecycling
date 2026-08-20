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
    private static readonly string _logDirectory;
    private static readonly string _logFilePath;

    static DiagnosticLogger()
    {
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        _logFilePath = Path.Combine(_logDirectory, "hardware_debug.log");

        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            // Write session start marker
            WriteToFile(
                $"\n======================================================\n" +
                $" SESSION STARTED: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                $" BaseDirectory: {AppContext.BaseDirectory}\n" +
                $"======================================================\n");
        }
        catch { }
    }

    public static string LogFilePath => _logFilePath;

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
    }

    private static void WriteToFile(string text)
    {
        lock (_syncLock)
        {
            try
            {
                File.AppendAllText(_logFilePath, text + Environment.NewLine);
            }
            catch { }
        }
    }
}
