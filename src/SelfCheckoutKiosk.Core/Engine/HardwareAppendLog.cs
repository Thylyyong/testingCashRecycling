using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

/// <summary>
/// Append-only financial audit log for cash-in-escrow events (Blueprint §4).
///
/// SECURITY RULES — do not violate:
///   1. This handler MUST be the first subscriber wired to ICashRecycler.OnNoteInEscrow
///      inside LLCoreLogicEngine.InitializeAsync. Every other handler runs after this.
///   2. Every write uses FileOptions.WriteThrough + Flush(flushToDisk:true) to bypass
///      the OS write cache. The record must survive an immediate power loss.
///   3. The log file path is constructor-injected — NEVER hard-coded here.
///      The path must be on the BitLocker-protected OS volume (§5 OS Hardening — Lead action).
///   4. This class lives in Core and has no reference to Infrastructure or Hal.Vendor.*.
///
/// Log format (one record per line, pipe-delimited, UTF-8):
///   {ISO-8601-UTC}|IN_ESCROW|{escrowId:N}|{amountUsd:F2}
///   {ISO-8601-UTC}|COMMITTED_TO_VAULT|{escrowId:N}
///   {ISO-8601-UTC}|REJECTED|{escrowId:N}
///
/// ScanOrphans() on startup returns any EscrowId whose IN_ESCROW entry has
/// no paired COMMITTED_TO_VAULT or REJECTED — potential lost-cash events that
/// must be surfaced to the Admin Diagnostics Panel (§5).
/// </summary>
public sealed class HardwareAppendLog
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    private const string EventInEscrow         = "IN_ESCROW";
    private const string EventCommittedToVault = "COMMITTED_TO_VAULT";
    private const string EventRejected         = "REJECTED";

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------
    private readonly string _logPath;
    private readonly object _writeLock = new();

    // The escrow id of the most-recently recorded IN_ESCROW entry.
    // Safe to read from the engine's OnNoteInEscrow handler immediately after
    // this handler returns, because the cash recycler puts only one note into
    // escrow at a time (physical hardware constraint).
    private Guid _lastEscrowId;

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------
    /// <param name="logPath">
    /// Absolute path to the append log file. The directory is created if it
    /// does not exist. Must be on the BitLocker-protected OS volume (Lead/§5).
    /// </param>
    public HardwareAppendLog(string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        _logPath = logPath;

        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    // -----------------------------------------------------------------------
    // Event handler — wire this FIRST to ICashRecycler.OnNoteInEscrow
    // -----------------------------------------------------------------------
    /// <summary>
    /// Synchronous event handler for <see cref="ICashRecycler.OnNoteInEscrow"/>.
    /// Wire to the cash recycler event BEFORE any other subscriber so the
    /// audit record is written before any business processing of the note.
    /// After this returns, read <see cref="LastEscrowId"/> to obtain the
    /// EscrowId needed for CommitToVault or Reject.
    /// </summary>
    public void HandleNoteInEscrow(object? sender, NoteInEscrowEventArgs e)
    {
        var escrowId = Guid.NewGuid();
        WriteEntry($"{EventInEscrow}|{escrowId:N}|{e.Note.Amount:F2}");
        _lastEscrowId = escrowId;
    }

    /// <summary>EscrowId written by the most recent <see cref="HandleNoteInEscrow"/> call.</summary>
    public Guid LastEscrowId => _lastEscrowId;

    // -----------------------------------------------------------------------
    // Resolution commands
    // -----------------------------------------------------------------------
    /// <summary>
    /// Writes a COMMITTED_TO_VAULT record paired with the given escrow id.
    /// Call this after the engine confirms the note has been accepted into the vault.
    /// </summary>
    public void CommitToVault(Guid escrowId)
        => WriteEntry($"{EventCommittedToVault}|{escrowId:N}");

    /// <summary>
    /// Writes a REJECTED record paired with the given escrow id.
    /// Call this after the engine instructs the recycler to return the escrowed note.
    /// </summary>
    public void Reject(Guid escrowId)
        => WriteEntry($"{EventRejected}|{escrowId:N}");

    // -----------------------------------------------------------------------
    // Startup reconciliation
    // -----------------------------------------------------------------------
    /// <summary>
    /// Reads the log and returns every EscrowId that has an IN_ESCROW record
    /// but no paired COMMITTED_TO_VAULT or REJECTED resolution.
    /// Call once during engine startup; surface results to the Admin Diagnostics Panel.
    /// </summary>
    public IReadOnlyList<Guid> ScanOrphans()
    {
        if (!File.Exists(_logPath))
            return [];

        var inEscrow  = new HashSet<Guid>();
        var resolved  = new HashSet<Guid>();

        foreach (var line in File.ReadLines(_logPath))
        {
            // Line format: {timestamp}|{eventType}|{escrowId:N}[|{extra}]
            var parts = line.Split('|');
            if (parts.Length < 3) continue;

            // parts[0] = ISO-8601 timestamp (no pipe chars in O format)
            // parts[1] = event type
            // parts[2] = escrowId (Guid N format — 32 hex, no hyphens)
            var eventType = parts[1];
            if (!Guid.TryParseExact(parts[2], "N", out var id)) continue;

            if (eventType == EventInEscrow)                                   inEscrow.Add(id);
            else if (eventType is EventCommittedToVault or EventRejected)     resolved.Add(id);
        }

        inEscrow.ExceptWith(resolved);
        return [.. inEscrow];
    }

    // -----------------------------------------------------------------------
    // Core write primitive
    // -----------------------------------------------------------------------
    private void WriteEntry(string payload)
    {
        // Full line: {ISO-8601-UTC}|{payload}\n
        // DateTimeOffset "O" format never contains a pipe character.
        var line  = $"{DateTimeOffset.UtcNow:O}|{payload}{Environment.NewLine}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);

        // SECURITY: FileOptions.WriteThrough bypasses the OS write cache.
        //           Flush(flushToDisk:true) additionally issues a FlushFileBuffers
        //           syscall so the data is physically on the storage medium before
        //           this method returns. The record survives an immediate power loss.
        lock (_writeLock)
        {
            using var fs = new FileStream(
                _logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.WriteThrough);

            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
    }
}
