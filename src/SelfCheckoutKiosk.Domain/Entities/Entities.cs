using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Domain.Entities;

// TODO(Back-End): flesh out per Blueprint §3. Minimal for Sprint 0.

public sealed class Product
{
    public required string Ean13 { get; init; }
    public required string Description { get; init; }
    public decimal UsdPrice { get; init; }
}

public sealed class Transaction
{
    public Guid TransactionGuid { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public decimal TotalUsd { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
}

public sealed class LicenseConfiguration
{
    public required string HardwareId { get; init; }
    public required string Tier { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public int MaxKiosks { get; init; }
}
