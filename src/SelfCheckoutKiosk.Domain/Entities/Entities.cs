using System;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Domain.Entities;

/// <summary>
/// A product that can be found by scanning its EAN-13 barcode.
/// </summary>
public sealed class Product
{
    /// <summary>
    /// Local database identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The product's 13-digit EAN barcode.
    /// </summary>
    public required string Ean13 { get; set; }

    /// <summary>
    /// Product name or customer-facing description.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Product price in USD.
    /// USD is the kiosk's ledger currency.
    /// </summary>
    public decimal UsdPrice { get; set; }

    /// <summary>
    /// Determines whether this product may be sold.
    ///
    /// An inactive product can remain in historical transactions
    /// without being available for new purchases.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One customer checkout transaction.
///
/// A completed transaction is first saved locally with
/// SyncStatus.Pending and synchronized with the central server later.
/// </summary>
public sealed class Transaction
{
    /// <summary>
    /// Stable transaction identifier used for local persistence,
    /// hardware-audit correlation, and idempotent synchronization.
    /// </summary>
    public Guid TransactionGuid { get; set; } =
        Guid.NewGuid();

    /// <summary>
    /// Time at which the transaction was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    /// <summary>
    /// Time at which payment and local persistence completed.
    ///
    /// Null while the transaction is still in progress.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Full amount the customer must pay, in USD.
    /// </summary>
    public decimal TotalUsd { get; set; }

    /// <summary>
    /// Running total of accepted customer tender, in USD.
    /// </summary>
    public decimal TenderedUsd { get; set; }

    /// <summary>
    /// Whole-dollar change returned as USD.
    /// </summary>
    public decimal ChangeUsd { get; set; }

    /// <summary>
    /// Fractional-dollar change converted and dispensed as KHR.
    /// </summary>
    public decimal ChangeKhr { get; set; }

    /// <summary>
    /// KHR-per-USD exchange rate used for this transaction.
    ///
    /// The value is saved so the transaction can be audited later
    /// even when the current exchange rate changes.
    /// </summary>
    public decimal ExchangeRateKhrPerUsd { get; set; }

    /// <summary>
    /// Business status of the transaction.
    /// </summary>
    public TransactionStatus Status { get; set; } =
        TransactionStatus.InProgress;

    /// <summary>
    /// Synchronization status with the central server.
    /// </summary>
    public SyncStatus SyncStatus { get; set; } =
        SyncStatus.Pending;
}

/// <summary>
/// One purchased product line inside a transaction.
///
/// Snapshot fields preserve the product description and price that
/// existed at the exact time of purchase.
/// </summary>
public sealed class TransactionLine
{
    /// <summary>
    /// Local database identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign-key value linking this line to its transaction.
    /// </summary>
    public Guid TransactionGuid { get; set; }

    /// <summary>
    /// Local identifier of the purchased product.
    /// </summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Product description captured when the sale occurred.
    /// </summary>
    public required string DescriptionSnapshot { get; set; }

    /// <summary>
    /// Product unit price captured when the sale occurred.
    /// </summary>
    public decimal UnitPriceUsdSnapshot { get; set; }

    /// <summary>
    /// Number of units purchased.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// UnitPriceUsdSnapshot multiplied by Quantity.
    /// </summary>
    public decimal LineTotalUsd { get; set; }
}

/// <summary>
/// Persisted, validated license configuration for this kiosk.
///
/// Signature verification and tier enforcement belong to
/// OfflineLicenseManager in the Core project.
/// </summary>
public sealed class LicenseConfiguration
{
    /// <summary>
    /// Local database identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Hardware identity to which this license is node-locked.
    /// </summary>
    public required string HardwareId { get; set; }

    /// <summary>
    /// Lite, Pro, or Enterprise license tier.
    /// </summary>
    public LicenseTier Tier { get; set; }

    /// <summary>
    /// Maximum number of kiosk nodes permitted by the license.
    /// </summary>
    public int MaxKiosks { get; set; }

    /// <summary>
    /// Whether the signed license permits the cash module.
    ///
    /// OfflineLicenseManager must still enforce the tier rules.
    /// </summary>
    public bool CashModuleEnabled { get; set; }

    /// <summary>
    /// Whether the signed license permits AI camera telemetry.
    /// </summary>
    public bool AiModuleEnabled { get; set; }

    /// <summary>
    /// Whether the signed license permits custom ERP synchronization.
    /// </summary>
    public bool ErpSyncEnabled { get; set; }

    /// <summary>
    /// Time after which the license is no longer valid.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// Original signed token or persisted validated license payload.
    ///
    /// The private signing key must never be stored here.
    /// </summary>
    public string SignedToken { get; set; } =
        string.Empty;
}