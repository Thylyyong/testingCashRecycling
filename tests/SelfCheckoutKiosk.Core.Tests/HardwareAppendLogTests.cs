using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

/// <summary>
/// Unit tests for <see cref="HardwareAppendLog"/> (Blueprint §4).
///
/// AI NOTE — before editing this file:
///   1. Re-read Blueprint §4 (HardwareAppendLog section) and the pending tracker.
///   2. FileOptions.WriteThrough + Flush(flushToDisk:true) are production
///      requirements — do not remove them in the implementation to "speed up tests".
///   3. These tests use real temp files to validate actual disk writes.
///      No BitLocker, no OS config — just regular temp directory writes.
/// </summary>
public sealed class HardwareAppendLogTests : IDisposable
{
    // Each test gets a unique log file in the system temp folder.
    private readonly string _logPath =
        Path.Combine(Path.GetTempPath(), $"hal_test_{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        if (File.Exists(_logPath))
            File.Delete(_logPath);
    }

    // -----------------------------------------------------------------------
    // Escrow write
    // -----------------------------------------------------------------------

    [Fact]
    public void HandleNoteInEscrow_WritesInEscrowEntry()
    {
        var log = new HardwareAppendLog(_logPath);

        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(1.00m)));

        var lines = File.ReadAllLines(_logPath);
        Assert.Single(lines);
        Assert.Contains("IN_ESCROW", lines[0]);
        Assert.Contains("1.00", lines[0]);
    }

    [Fact]
    public void HandleNoteInEscrow_SetsLastEscrowId()
    {
        var log = new HardwareAppendLog(_logPath);

        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(5.00m)));

        Assert.NotEqual(Guid.Empty, log.LastEscrowId);
    }

    [Fact]
    public void HandleNoteInEscrow_EscrowIdAppearsInLogLine()
    {
        var log = new HardwareAppendLog(_logPath);

        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(20.00m)));
        var escrowId = log.LastEscrowId;

        var content = File.ReadAllText(_logPath);
        Assert.Contains(escrowId.ToString("N"), content);
    }

    // -----------------------------------------------------------------------
    // Resolution — CommitToVault
    // -----------------------------------------------------------------------

    [Fact]
    public void CommitToVault_WritesCommittedEntry_PairedByEscrowId()
    {
        var log = new HardwareAppendLog(_logPath);
        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(1.00m)));
        var escrowId = log.LastEscrowId;

        log.CommitToVault(escrowId);

        var lines = File.ReadAllLines(_logPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("COMMITTED_TO_VAULT", lines[1]);
        Assert.Contains(escrowId.ToString("N"), lines[1]);
    }

    // -----------------------------------------------------------------------
    // Resolution — Reject
    // -----------------------------------------------------------------------

    [Fact]
    public void Reject_WritesRejectedEntry_PairedByEscrowId()
    {
        var log = new HardwareAppendLog(_logPath);
        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(1.00m)));
        var escrowId = log.LastEscrowId;

        log.Reject(escrowId);

        var lines = File.ReadAllLines(_logPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("REJECTED", lines[1]);
        Assert.Contains(escrowId.ToString("N"), lines[1]);
    }

    // -----------------------------------------------------------------------
    // ScanOrphans — startup reconciliation
    // -----------------------------------------------------------------------

    [Fact]
    public void ScanOrphans_NoLog_ReturnsEmpty()
    {
        // Log file does not exist yet.
        var log = new HardwareAppendLog(_logPath);

        var orphans = log.ScanOrphans();

        Assert.Empty(orphans);
    }

    [Fact]
    public void ScanOrphans_ResolvedEntry_ReturnsEmpty()
    {
        var log = new HardwareAppendLog(_logPath);
        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(1.00m)));
        var id = log.LastEscrowId;
        log.CommitToVault(id);

        var orphans = log.ScanOrphans();

        Assert.Empty(orphans);
    }

    [Fact]
    public void ScanOrphans_UnresolvedEscrow_ReturnsOrphanId()
    {
        // Simulate: power was lost after escrow was written, before commit.
        var log = new HardwareAppendLog(_logPath);
        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(1.00m)));
        var orphanId = log.LastEscrowId;
        // Deliberately do NOT call CommitToVault or Reject.

        var orphans = log.ScanOrphans();

        Assert.Single(orphans);
        Assert.Equal(orphanId, orphans[0]);
    }

    [Fact]
    public void ScanOrphans_MixedResolved_ReturnsOnlyUnresolved()
    {
        var log = new HardwareAppendLog(_logPath);

        // Note 1 → resolved
        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(1.00m)));
        var resolvedId = log.LastEscrowId;
        log.CommitToVault(resolvedId);

        // Note 2 → orphan
        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(5.00m)));
        var orphanId = log.LastEscrowId;

        var orphans = log.ScanOrphans();

        Assert.Single(orphans);
        Assert.Equal(orphanId, orphans[0]);
    }

    // -----------------------------------------------------------------------
    // Multiple sequential writes
    // -----------------------------------------------------------------------

    [Fact]
    public void HandleNoteInEscrow_MultipleNotes_EachGetsUniqueEscrowId()
    {
        var log = new HardwareAppendLog(_logPath);

        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(1.00m)));
        var id1 = log.LastEscrowId;

        log.HandleNoteInEscrow(sender: null, new NoteInEscrowEventArgs(Money.Usd(5.00m)));
        var id2 = log.LastEscrowId;

        Assert.NotEqual(id1, id2);

        var lines = File.ReadAllLines(_logPath);
        Assert.Equal(2, lines.Length);
    }
}
