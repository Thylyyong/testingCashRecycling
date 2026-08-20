using System.Globalization;
using System.Text;
using System.Threading;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

/// <summary>
/// Power-loss-immune append-only audit trail for cash escrow events
/// (Blueprint §4). Every note that lands in the cash recycler's escrow slot
/// is logged the INSTANT it lands — <see cref="Attach"/> hooks
/// <see cref="ICashRecycler.OnNoteInEscrow"/> directly, synchronously,
/// independent of the engine's own business-logic dispatch on the same
/// event. That's deliberate: this is a passive durability tap, not a second
/// business-logic dispatcher, so it doesn't compete with "the engine is the
/// single subscriber that drives state transitions from hardware events" —
/// it just also listens, and never mutates engine state.
///
/// Every escrow entry must eventually be closed by exactly one resolution —
/// <see cref="LogCommittedToVault"/> or <see cref="LogRejected"/> — called by
/// the engine once it knows the outcome. A power cut between ESCROW_ACCEPTED
/// and its resolution leaves an orphaned entry; the constructor scans the
/// existing log file for exactly these on startup and exposes the count via
/// <see cref="UnresolvedEntriesAtStartup"/>. Reconciling that count against
/// the PHYSICAL cassette note count (to attribute the loss precisely) remains
/// a TODO(Back-End) — this type only detects and counts the anomaly.
///
/// Sequencing assumption: the cash recycler hardware holds ONE note in escrow
/// at a time (true of every vendor SKU this project targets today), so a
/// resolution call closes the single most recently opened, still-unresolved
/// entry rather than needing a note-level correlation id — the HAL contract
/// (<see cref="NoteInEscrowEventArgs"/>) doesn't carry one.
/// </summary>
public sealed class HardwareAppendLog : IDisposable
{
    private readonly FileStream _stream;
    private readonly Lock _gate = new();
    private long _nextSequenceId = 1;
    private long? _openSequenceId;

    public HardwareAppendLog(string logFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        string? directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        UnresolvedEntriesAtStartup = ReconcileExistingLog(logFilePath, out long maxSequenceIdSeen);
        _nextSequenceId = maxSequenceIdSeen + 1;

        // WriteThrough tells the OS to skip its write-behind cache for this
        // handle; the explicit Flush(flushToDisk: true) after every write
        // below then forces the storage device itself to commit the write
        // rather than acknowledging from its own cache. Together they're what
        // make a write survive an INSTANTANEOUS power loss, not just a
        // process crash.
        _stream = new FileStream(logFilePath, new FileStreamOptions
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.WriteThrough,
        });
    }

    /// <summary>Count of ESCROW_ACCEPTED entries from PRIOR process sessions
    /// (i.e. already in the log file before this instance opened it) that were
    /// never closed by a matching COMMITTED_TO_VAULT/REJECTED — evidence of a
    /// power cut between escrow and resolution. Computed once at startup;
    /// feeds <c>AdminDiagnosticsSnapshot.UnresolvedHardwareAppendLogEntries</c>.</summary>
    public int UnresolvedEntriesAtStartup { get; }

    /// <summary>Scans a pre-existing log file for unresolved escrow entries
    /// and reports the highest sequence id seen, so this session's numbering
    /// continues rather than restarting at 1 and colliding with history.</summary>
    private static int ReconcileExistingLog(string logFilePath, out long maxSequenceIdSeen)
    {
        maxSequenceIdSeen = 0;
        if (!File.Exists(logFilePath))
            return 0;

        var openEscrowSequenceIds = new HashSet<long>();

        foreach (string line in File.ReadLines(logFilePath))
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 3) continue;
            if (!long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequenceId))
                continue;

            if (sequenceId > maxSequenceIdSeen)
                maxSequenceIdSeen = sequenceId;

            switch (fields[2])
            {
                case "ESCROW_ACCEPTED":
                    openEscrowSequenceIds.Add(sequenceId);
                    break;
                case "COMMITTED_TO_VAULT":
                case "REJECTED":
                    openEscrowSequenceIds.Remove(sequenceId);
                    break;
            }
        }

        return openEscrowSequenceIds.Count;
    }

    /// <summary>True while an ESCROW_ACCEPTED entry from THIS process session
    /// has not yet been resolved by a COMMITTED_TO_VAULT/REJECTED call —
    /// surfaced by the admin diagnostics panel as a live orphan signal.
    /// Reconciling orphans left behind by a power cut in a PRIOR session
    /// against the physical cassette count on startup remains the
    /// TODO(Back-End) noted above; this only sees the current session.</summary>
    public bool HasUnresolvedEscrowEntry
    {
        get { lock (_gate) return _openSequenceId is not null; }
    }

    public void Attach(ICashRecycler cashRecycler) => cashRecycler.OnNoteInEscrow += HandleNoteInEscrow;

    public void Detach(ICashRecycler cashRecycler) => cashRecycler.OnNoteInEscrow -= HandleNoteInEscrow;

    private void HandleNoteInEscrow(object? sender, NoteInEscrowEventArgs e)
    {
        lock (_gate)
        {
            long sequenceId = _nextSequenceId++;
            _openSequenceId = sequenceId;
            AppendEntryLocked(sequenceId, "ESCROW_ACCEPTED", e.Note, detail: null);
        }
    }

    /// <summary>Closes the open escrow entry: the note was kept and counted
    /// toward the tender.</summary>
    public void LogCommittedToVault(Money note) => LogResolution("COMMITTED_TO_VAULT", note, detail: null);

    /// <summary>Closes the open escrow entry: the note was physically
    /// returned to the customer.</summary>
    public void LogRejected(Money note, string reason) => LogResolution("REJECTED", note, reason);

    /// <summary>Books the sub-100-KHR remainder discarded by always-round-down
    /// change calculation as a "kiosk float gain" audit entry — never silently
    /// dropped, per Blueprint §3.</summary>
    public void LogFloatGain(decimal discardedKhrRemainder)
    {
        if (discardedKhrRemainder <= 0) return;

        lock (_gate)
        {
            long sequenceId = _nextSequenceId++;
            AppendEntryLocked(sequenceId, "KIOSK_FLOAT_GAIN", new Money(discardedKhrRemainder, CurrencyCode.Khr), detail: null);
        }
    }

    private void LogResolution(string eventName, Money note, string? detail)
    {
        lock (_gate)
        {
            long sequenceId;
            if (_openSequenceId is { } open)
            {
                sequenceId = open;
                _openSequenceId = null;
            }
            else
            {
                // No open escrow entry to resolve — log it anyway, flagged,
                // so reconciliation can surface the anomaly instead of
                // silently losing the event.
                sequenceId = _nextSequenceId++;
                detail = detail is null ? "UNPAIRED" : $"UNPAIRED;{detail}";
            }

            AppendEntryLocked(sequenceId, eventName, note, detail);
        }
    }

    private void AppendEntryLocked(long sequenceId, string eventName, Money note, string? detail)
    {
        string line = string.Join('\t',
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            sequenceId.ToString(CultureInfo.InvariantCulture),
            eventName,
            note.Currency.ToString(),
            note.Amount.ToString(CultureInfo.InvariantCulture),
            detail ?? string.Empty) + Environment.NewLine;

        byte[] bytes = Encoding.UTF8.GetBytes(line);
        _stream.Write(bytes, 0, bytes.Length);
        _stream.Flush(flushToDisk: true);
    }

    public void Dispose() => _stream.Dispose();
}
