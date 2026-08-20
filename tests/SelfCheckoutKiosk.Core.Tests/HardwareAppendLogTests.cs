using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class HardwareAppendLogTests : IDisposable
{
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"hwlog-{Guid.NewGuid():N}.tsv");

    [Fact]
    public void CommittedResolution_ClosesOpenEscrowEntry()
    {
        using var log = new HardwareAppendLog(_logPath);
        var recycler = new FakeCashRecycler();
        log.Attach(recycler);

        recycler.RaiseNoteInEscrow(Money.Usd(5m));
        Assert.True(log.HasUnresolvedEscrowEntry);

        log.LogCommittedToVault(Money.Usd(5m));
        Assert.False(log.HasUnresolvedEscrowEntry);
    }

    [Fact]
    public void RejectedResolution_ClosesOpenEscrowEntry()
    {
        using var log = new HardwareAppendLog(_logPath);
        var recycler = new FakeCashRecycler();
        log.Attach(recycler);

        recycler.RaiseNoteInEscrow(Money.Usd(5m));
        log.LogRejected(Money.Usd(5m), "test");
        Assert.False(log.HasUnresolvedEscrowEntry);
    }

    [Fact]
    public void NewInstance_ReconcilesOrphanedEntriesFromPriorSession()
    {
        var recycler = new FakeCashRecycler();
        using (var firstSession = new HardwareAppendLog(_logPath))
        {
            firstSession.Attach(recycler);
            recycler.RaiseNoteInEscrow(Money.Usd(5m)); // never resolved -> simulated power loss
        }

        using var secondSession = new HardwareAppendLog(_logPath);
        Assert.Equal(1, secondSession.UnresolvedEntriesAtStartup);
    }

    [Fact]
    public void NewInstance_WithNoOrphans_ReportsZero()
    {
        var recycler = new FakeCashRecycler();
        using (var firstSession = new HardwareAppendLog(_logPath))
        {
            firstSession.Attach(recycler);
            recycler.RaiseNoteInEscrow(Money.Usd(5m));
            firstSession.LogCommittedToVault(Money.Usd(5m));
        }

        using var secondSession = new HardwareAppendLog(_logPath);
        Assert.Equal(0, secondSession.UnresolvedEntriesAtStartup);
    }

    [Fact]
    public void LogFloatGain_ZeroOrNegative_DoesNotThrow()
    {
        using var log = new HardwareAppendLog(_logPath);
        log.LogFloatGain(0m);
        log.LogFloatGain(-10m);
    }

    public void Dispose()
    {
        if (File.Exists(_logPath))
            File.Delete(_logPath);
    }

#pragma warning disable CS0067 // raised by the interface contract but unused by these tests
    private sealed class FakeCashRecycler : ICashRecycler
    {
        public event EventHandler<NoteInsertedEventArgs>? OnNoteInserted;
        public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
        public event EventHandler<HardwareFaultEventArgs>? OnFault;
        public event EventHandler<CashAcceptorStateChangedEventArgs>? OnAcceptorStateChanged;
        public event EventHandler<CashRecyclerJamEventArgs>? OnJam;
        public event EventHandler<CassetteInventoryChangedEventArgs>? OnCassetteInventoryChanged;

        public void RaiseNoteInEscrow(Money note) => OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(note));

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ArmAcceptanceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAcceptingCashAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken cancellationToken = default)
            => Task.FromResult(new DispenseResult(true, change));
        public Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
#pragma warning restore CS0067
}
