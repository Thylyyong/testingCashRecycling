using System.Collections.Concurrent;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;
using Xunit;

namespace SelfCheckoutKiosk_Integration_Tests;

public sealed class CashDeviceServerSupervisorTests
{
    [Fact]
    public async Task AlreadyHealthyServer_IsReusedAndNotOwned()
    {
        var probe = new FakeHealthProbe(defaultResult: true);
        var factory = new FakeProcessFactory();
        var supervisor = CreateSupervisor(
            executablePath: "missing-but-not-needed.exe",
            probe,
            factory);

        await supervisor.EnsureReadyAsync();

        Assert.Equal(CashDeviceServerState.Running, supervisor.State);
        Assert.False(supervisor.OwnedByKiosk);
        Assert.Equal(0, factory.StartCount);

        await supervisor.StopAsync();
        Assert.Equal(0, factory.Process.StopCount);
    }

    [Fact]
    public async Task StoppedServer_IsStartedAndWaitedUntilHttpReady()
    {
        using var executable = new TemporaryExecutable();
        var probe = new FakeHealthProbe(
            defaultResult: true,
            false,
            false);
        var factory = new FakeProcessFactory();
        var states = new List<CashDeviceServerState>();
        var supervisor = CreateSupervisor(executable.Path, probe, factory);
        supervisor.StateChanged += (_, eventArgs) => states.Add(eventArgs.State);

        await supervisor.EnsureReadyAsync();

        Assert.Equal(1, factory.StartCount);
        Assert.True(supervisor.OwnedByKiosk);
        Assert.Equal(CashDeviceServerState.Running, supervisor.State);
        Assert.Equal(
            new[]
            {
                CashDeviceServerState.Starting,
                CashDeviceServerState.Running
            },
            states);
    }

    [Fact]
    public async Task ProcessNeverBecomesReady_TimesOutAndStopsOwnedProcess()
    {
        using var executable = new TemporaryExecutable();
        var probe = new FakeHealthProbe(defaultResult: false);
        var factory = new FakeProcessFactory();
        var supervisor = CreateSupervisor(
            executable.Path,
            probe,
            factory,
            startupTimeout: TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAsync<TimeoutException>(
            () => supervisor.EnsureReadyAsync());

        Assert.Equal(CashDeviceServerState.Failed, supervisor.State);
        Assert.False(supervisor.OwnedByKiosk);
        Assert.Equal(1, factory.Process.StopCount);
    }

    [Fact]
    public async Task MissingExecutable_FailsWithoutStartingProcess()
    {
        var probe = new FakeHealthProbe(defaultResult: false);
        var factory = new FakeProcessFactory();
        var supervisor = CreateSupervisor(
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N"),
                "CashDevice-RestAPI.exe"),
            probe,
            factory);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => supervisor.EnsureReadyAsync());

        Assert.Equal(CashDeviceServerState.Failed, supervisor.State);
        Assert.Equal(0, factory.StartCount);
    }

    [Fact]
    public async Task KioskOwnedProcess_IsStoppedOnShutdown()
    {
        using var executable = new TemporaryExecutable();
        var probe = new FakeHealthProbe(defaultResult: true, false);
        var factory = new FakeProcessFactory();
        var supervisor = CreateSupervisor(executable.Path, probe, factory);
        await supervisor.EnsureReadyAsync();

        await supervisor.StopAsync();

        Assert.Equal(1, factory.Process.StopCount);
        Assert.False(supervisor.OwnedByKiosk);
        Assert.Equal(CashDeviceServerState.NotRunning, supervisor.State);
    }

    [Fact]
    public async Task ConcurrentStartRequests_CreateOnlyOneProcess()
    {
        using var executable = new TemporaryExecutable();
        var probe = new FakeHealthProbe(defaultResult: true, false);
        var factory = new FakeProcessFactory();
        var supervisor = CreateSupervisor(executable.Path, probe, factory);

        await Task.WhenAll(
            supervisor.EnsureReadyAsync(),
            supervisor.EnsureReadyAsync());

        Assert.Equal(1, factory.StartCount);
        await supervisor.StopAsync();
    }

    private static CashDeviceServerSupervisor CreateSupervisor(
        string executablePath,
        ICashDeviceServerHealthProbe probe,
        ICashDeviceServerProcessFactory factory,
        TimeSpan? startupTimeout = null)
    {
        return new CashDeviceServerSupervisor(
            new CashDeviceServerOptions
            {
                BaseUri = new Uri("http://127.0.0.1:3000/"),
                ExecutablePath = executablePath,
                Version = "V1.6.1-RC.4",
                StartupTimeout = startupTimeout ?? TimeSpan.FromSeconds(1),
                ProbeInterval = TimeSpan.FromMilliseconds(5),
                ProbeRequestTimeout = TimeSpan.FromMilliseconds(20),
                ShutdownTimeout = TimeSpan.FromMilliseconds(50)
            },
            probe,
            factory);
    }

    private sealed class FakeHealthProbe : ICashDeviceServerHealthProbe
    {
        private readonly ConcurrentQueue<bool> _responses;
        private readonly bool _defaultResult;

        public FakeHealthProbe(bool defaultResult, params bool[] responses)
        {
            _defaultResult = defaultResult;
            _responses = new ConcurrentQueue<bool>(responses);
        }

        public Task<bool> IsHealthyAsync(
            Uri baseUri,
            TimeSpan requestTimeout,
            CancellationToken cancellationToken = default)
        {
            _ = baseUri;
            _ = requestTimeout;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _responses.TryDequeue(out var response)
                    ? response
                    : _defaultResult);
        }
    }

    private sealed class FakeProcessFactory : ICashDeviceServerProcessFactory
    {
        public FakeProcess Process { get; } = new();
        public int StartCount { get; private set; }

        public ICashDeviceServerProcess Start(
            string executablePath,
            string workingDirectory)
        {
            _ = executablePath;
            _ = workingDirectory;
            StartCount++;
            return Process;
        }
    }

    private sealed class FakeProcess : ICashDeviceServerProcess
    {
        public bool HasExited { get; set; }
        public int StopCount { get; private set; }

        public Task StopAsync(
            TimeSpan shutdownTimeout,
            CancellationToken cancellationToken = default)
        {
            _ = shutdownTimeout;
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            HasExited = true;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TemporaryExecutable : IDisposable
    {
        public TemporaryExecutable()
        {
            DirectoryPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SelfCheckoutKiosk-ServerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Path = System.IO.Path.Combine(
                DirectoryPath,
                "CashDevice-RestAPI.exe");
            File.WriteAllBytes(Path, Array.Empty<byte>());
        }

        public string DirectoryPath { get; }
        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
