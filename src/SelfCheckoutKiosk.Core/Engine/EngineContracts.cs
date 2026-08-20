using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

public sealed class KioskStateChangedEventArgs(KioskState previous, KioskState current) : EventArgs
{
    public KioskState Previous { get; } = previous;
    public KioskState Current { get; } = current;
}

public sealed class ProductAddedEventArgs(Product product, decimal runningTotalUsd) : EventArgs
{
    public Product Product { get; } = product;
    public decimal RunningTotalUsd { get; } = runningTotalUsd;
}

public sealed class BalanceChangedEventArgs(decimal totalUsd, decimal tenderedUsd, decimal remainingUsd) : EventArgs
{
    public decimal TotalUsd { get; } = totalUsd;
    public decimal TenderedUsd { get; } = tenderedUsd;
    public decimal RemainingUsd { get; } = remainingUsd;
}

/// <summary>Raised once per note as the engine resolves it during cash
/// ingestion — the UI's note-by-note escrow feedback. <see cref="Accepted"/>
/// distinguishes a committed-to-vault note from one rejected under
/// exact-cash-only lockout; <see cref="TenderedUsd"/>/<see cref="RemainingUsd"/>
/// reflect the running total AFTER this note was resolved.</summary>
public sealed class NoteProcessedEventArgs(Money note, bool accepted, decimal tenderedUsd, decimal remainingUsd) : EventArgs
{
    public Money Note { get; } = note;
    public bool Accepted { get; } = accepted;
    public decimal TenderedUsd { get; } = tenderedUsd;
    public decimal RemainingUsd { get; } = remainingUsd;
}

/// <summary>Result of routing + handling a single raw scan.</summary>
public sealed record ScanResult(ScanCategory Category, bool Accepted, string? Message = null);

/// <summary>Point-in-time snapshot for the admin diagnostics panel — hardware
/// link state, cassette counts, audit-log health, and whatever license info
/// is currently available. <see cref="LicenseTier"/>/<see cref="LicenseExpiresAtUtc"/>
/// degrade to "Unknown"/null rather than blocking on Licensing being wired up.</summary>
public sealed record AdminDiagnosticsSnapshot(
    bool CashRecyclerConnected,
    bool BarcodeScannerConnected,
    bool ReceiptPrinterConnected,
    IReadOnlyDictionary<int, int> KhrCassetteCounts,
    int UnresolvedHardwareAppendLogEntries,
    string LicenseTier,
    DateTimeOffset? LicenseExpiresAtUtc,
    DateTimeOffset? LastSyncTimestampUtc);
