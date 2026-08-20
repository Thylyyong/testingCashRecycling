namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Port for a stable per-machine identifier used for license node-lock
/// (Blueprint §3). The production implementation (TPM/motherboard-backed)
/// lives in Infrastructure — Core depends only on this abstraction so the
/// licensing logic never touches a concrete hardware API.
/// </summary>
public interface IHardwareIdProvider
{
    string GetHardwareId();
}
