using System.Diagnostics;

namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Starts and supervises only the ITL REST host process. Cash commands and
/// transaction policy remain in the existing REST client and HAL adapter.
/// </summary>
public sealed class CashDeviceServerSupervisor
    : ICashDeviceServerSupervisor
{
    private readonly CashDeviceServerOptions _options;
    private readonly ICashDeviceServerHealthProbe _healthProbe;
    private readonly ICashDeviceServerProcessFactory _processFactory;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stateSync = new();

    private ICashDeviceServerProcess? _ownedProcess;
    private CashDeviceServerState _state = CashDeviceServerState.NotRunning;
    private bool _ownedByKiosk;

    public CashDeviceServerSupervisor(
        HttpClient httpClient,
        CashDeviceServerOptions options)
        : this(
            options,
            new HttpCashDeviceServerHealthProbe(httpClient),
            new SystemCashDeviceServerProcessFactory())
    {
    }

    public CashDeviceServerSupervisor(
        CashDeviceServerOptions options,
        ICashDeviceServerHealthProbe healthProbe,
        ICashDeviceServerProcessFactory processFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(healthProbe);
        ArgumentNullException.ThrowIfNull(processFactory);
        options.Validate();

        _options = options;
        _healthProbe = healthProbe;
        _processFactory = processFactory;
    }

    public event EventHandler<CashDeviceServerStateChangedEventArgs>?
        StateChanged;

    public CashDeviceServerState State
    {
        get
        {
            lock (_stateSync)
            {
                return _state;
            }
        }
    }

    public bool OwnedByKiosk
    {
        get
        {
            lock (_stateSync)
            {
                return _ownedByKiosk;
            }
        }
    }

    public string Version => _options.Version;

    public async Task EnsureReadyAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (State == CashDeviceServerState.Running)
            {
                return;
            }

            PublishState(CashDeviceServerState.Starting);

            try
            {
                if (await ProbeAsync(cancellationToken).ConfigureAwait(false))
                {
                    SetOwnership(ownedByKiosk: false);
                    PublishState(CashDeviceServerState.Running);
                    return;
                }

                var executablePath = Path.GetFullPath(_options.ExecutablePath);
                if (!File.Exists(executablePath))
                {
                    throw new FileNotFoundException(
                        "The configured ITL CashDevice REST executable was not found.",
                        executablePath);
                }

                var workingDirectory = Path.GetDirectoryName(executablePath) ??
                    throw new InvalidOperationException(
                        "The ITL CashDevice REST working directory could not be resolved.");

                _ownedProcess = _processFactory.Start(
                    executablePath,
                    workingDirectory);
                SetOwnership(ownedByKiosk: true);

                var deadline = DateTimeOffset.UtcNow + _options.StartupTimeout;

                while (DateTimeOffset.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_ownedProcess.HasExited)
                    {
                        throw new InvalidOperationException(
                            "The ITL CashDevice REST process exited before becoming ready.");
                    }

                    if (await ProbeAsync(cancellationToken).ConfigureAwait(false))
                    {
                        PublishState(CashDeviceServerState.Running);
                        return;
                    }

                    await Task.Delay(_options.ProbeInterval, cancellationToken)
                        .ConfigureAwait(false);
                }

                throw new TimeoutException(
                    "The ITL CashDevice REST process did not become HTTP-ready " +
                    "within the configured startup timeout.");
            }
            catch
            {
                await StopOwnedProcessAfterFailureAsync().ConfigureAwait(false);
                PublishState(CashDeviceServerState.Failed);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (_ownedByKiosk && _ownedProcess is not null)
            {
                try
                {
                    await _ownedProcess
                        .StopAsync(_options.ShutdownTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _ownedProcess.Dispose();
                    _ownedProcess = null;
                }
            }

            SetOwnership(ownedByKiosk: false);
            PublishState(CashDeviceServerState.NotRunning);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        return _healthProbe.IsHealthyAsync(
            _options.BaseUri,
            _options.ProbeRequestTimeout,
            cancellationToken);
    }

    private async Task StopOwnedProcessAfterFailureAsync()
    {
        if (!_ownedByKiosk || _ownedProcess is null)
        {
            SetOwnership(ownedByKiosk: false);
            return;
        }

        try
        {
            await _ownedProcess
                .StopAsync(_options.ShutdownTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // The startup failure remains primary; no owned process is reused.
        }
        finally
        {
            _ownedProcess.Dispose();
            _ownedProcess = null;
            SetOwnership(ownedByKiosk: false);
        }
    }

    private void SetOwnership(bool ownedByKiosk)
    {
        lock (_stateSync)
        {
            _ownedByKiosk = ownedByKiosk;
        }
    }

    private void PublishState(CashDeviceServerState state)
    {
        lock (_stateSync)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        StateChanged?.Invoke(
            this,
            new CashDeviceServerStateChangedEventArgs(state));
    }

    private sealed class HttpCashDeviceServerHealthProbe(HttpClient httpClient)
        : ICashDeviceServerHealthProbe
    {
        public async Task<bool> IsHealthyAsync(
            Uri baseUri,
            TimeSpan requestTimeout,
            CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(requestTimeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, baseUri);
                using var response = await httpClient
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }
    }

    private sealed class SystemCashDeviceServerProcessFactory
        : ICashDeviceServerProcessFactory
    {
        public ICashDeviceServerProcess Start(
            string executablePath,
            string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(startInfo) ??
                throw new InvalidOperationException(
                    "The ITL CashDevice REST process could not be started.");
            return new SystemCashDeviceServerProcess(process);
        }
    }

    private sealed class SystemCashDeviceServerProcess(Process process)
        : ICashDeviceServerProcess
    {
        public bool HasExited => process.HasExited;

        public async Task StopAsync(
            TimeSpan shutdownTimeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(shutdownTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "The owned ITL CashDevice REST process did not stop in time.",
                    exception);
            }
        }

        public void Dispose() => process.Dispose();
    }
}
