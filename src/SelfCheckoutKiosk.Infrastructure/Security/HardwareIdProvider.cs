using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.Infrastructure.Security;

/// <summary>
/// Stable per-machine hardware id for license node-lock (Blueprint §3).
/// Sprint-0 implementation hashes the Windows install's MachineGuid
/// (HKLM\SOFTWARE\Microsoft\Cryptography); falls back to the machine name on
/// non-Windows / restricted-registry dev environments so the graph still
/// resolves.
///
/// TODO(Back-End): MachineGuid is registry-writable by an administrator, so
/// it is an adequate Sprint-0 identifier but not a tamper-proof one. Harden to
/// a TPM-backed identifier (e.g. the TPM endorsement key certificate) before
/// this gates a paid tier in production.
/// </summary>
public sealed class HardwareIdProvider : IHardwareIdProvider
{
    public string GetHardwareId()
    {
        string seed = TryReadWindowsMachineGuid() ?? Environment.MachineName;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash);
    }

    private static string? TryReadWindowsMachineGuid()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
