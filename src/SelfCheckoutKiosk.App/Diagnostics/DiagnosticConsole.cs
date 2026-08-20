using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SelfCheckoutKiosk.App.Diagnostics;

internal static class DiagnosticConsole
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    public static void Initialize()
    {
        if (!AllocConsole())
        {
            return;
        }

        var standardOutput = new StreamWriter(
            Console.OpenStandardOutput())
        {
            AutoFlush = true
        };

        var standardError = new StreamWriter(
            Console.OpenStandardError())
        {
            AutoFlush = true
        };

        Console.SetOut(standardOutput);
        Console.SetError(standardError);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(
            "==============================================");
        Console.WriteLine(
            " SelfCheckoutKiosk Development Diagnostics");
        Console.WriteLine(
            "==============================================");
        Console.ResetColor();

        Console.WriteLine(
            $"Started: {DateTimeOffset.Now}");
        Console.WriteLine(
            $"BaseDirectory: {AppContext.BaseDirectory}");
        Console.WriteLine();
    }
}
