namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

public enum CashDeviceServerState
{
    NotRunning = 0,
    Starting = 1,
    Running = 2,
    Failed = 3
}

public sealed class CashDeviceServerStateChangedEventArgs(
    CashDeviceServerState state)
    : EventArgs
{
    public CashDeviceServerState State { get; } = state;
}

public interface ICashDeviceServerSupervisor
{
    event EventHandler<CashDeviceServerStateChangedEventArgs>? StateChanged;

    CashDeviceServerState State { get; }
    bool OwnedByKiosk { get; }
    string Version { get; }

    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface ICashDeviceServerHealthProbe
{
    Task<bool> IsHealthyAsync(
        Uri baseUri,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken = default);
}

public interface ICashDeviceServerProcess : IDisposable
{
    bool HasExited { get; }

    Task StopAsync(
        TimeSpan shutdownTimeout,
        CancellationToken cancellationToken = default);
}

public interface ICashDeviceServerProcessFactory
{
    ICashDeviceServerProcess Start(
        string executablePath,
        string workingDirectory);
}
