using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Domain.Entities;

public sealed class Product
{
    public required string Ean13 { get; init; }
    public required string Description { get; init; }

    /// <summary>Ledger price — USD is the ledger currency (Blueprint §3);
    /// this is what feeds <c>Transaction.TotalUsd</c>.</summary>
    public decimal UsdPrice { get; init; }

    /// <summary>Display-only KHR price shown alongside the USD price on the
    /// scan/cart screen. NOT used for change calculation — that always derives
    /// from <see cref="UsdPrice"/> via the live-cached rate in
    /// <c>DualCurrencyCalculator</c>, so a stale catalog price here can never
    /// cause the kiosk to under/over-dispense change.</summary>
    public decimal KhrPrice { get; init; }
}

/// <summary>One scanned/priced item within a <see cref="Transaction"/>. A
/// coupon line carries a negative <see cref="UnitPriceUsd"/>.</summary>
public sealed class LineItem
{
    public required string Ean13 { get; init; }
    public required string Description { get; init; }
    public required decimal UnitPriceUsd { get; init; }
    public int Quantity { get; init; } = 1;

    public decimal LineTotalUsd => UnitPriceUsd * Quantity;
}

public sealed class Transaction
{
    public Guid TransactionGuid { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<LineItem> LineItems { get; init; } = [];

    /// <summary>Sum of all line totals — the ledger amount owed, in USD.</summary>
    public decimal TotalUsd { get; set; }

    /// <summary>Running USD-equivalent amount tendered so far (cash escrow +
    /// confirmed digital payment). Compared against <see cref="TotalUsd"/> to
    /// decide when the transaction is fully paid.</summary>
    public decimal TenderedUsd { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
}

public sealed class LicenseConfiguration
{
    public required string HardwareId { get; init; }
    public required LicenseTier Tier { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public int MaxKiosks { get; init; }
}
