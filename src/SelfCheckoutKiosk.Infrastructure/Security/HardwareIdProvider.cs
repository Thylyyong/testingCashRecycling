namespace SelfCheckoutKiosk.Infrastructure.Security;

/// <summary>
/// STUB — stable per-machine hardware id for license node-lock (Blueprint §3).
/// TODO(Back-End): derive from TPM/motherboard identifiers; keep deterministic.
/// </summary>
public sealed class HardwareIdProvider
{
    public string GetHardwareId()
        => throw new NotImplementedException("TODO(Back-End): implement TPM-backed hardware id.");
}
