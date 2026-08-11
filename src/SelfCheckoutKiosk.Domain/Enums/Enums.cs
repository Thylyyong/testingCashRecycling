namespace SelfCheckoutKiosk.Domain.Enums;

/// <summary>
/// Synchronization state of a locally committed transaction.
/// </summary>
public enum SyncStatus
{
    /// <summary>
    /// Saved locally but not yet acknowledged by the central server.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Successfully acknowledged by the central server.
    /// </summary>
    Completed = 1
}

/// <summary>
/// Currencies supported by the kiosk.
/// </summary>
public enum CurrencyCode
{
    Usd = 0,
    Khr = 1
}

/// <summary>
/// Deterministic LLCoreLogicEngine state-machine states.
/// </summary>
public enum KioskState
{
    Idle = 0,
    Scanning = 1,
    AwaitingPayment = 2,
    ProcessingCash = 3,
    DispensingChange = 4,
    TransactionComplete = 5,
    ExactCashOnlyLockout = 6,
    Faulted = 7
}

/// <summary>
/// Payment methods currently supported by the kiosk.
/// </summary>
public enum PaymentMethod
{
    Cash = 0,
    KhqrDigital = 1
}

/// <summary>
/// Classification produced by RegexRouter for every scan.
/// </summary>
public enum ScanCategory
{
    Unknown = 0,
    Ean13Product = 1,
    KhqrProfile = 2,
    OfflineCoupon = 3
}

/// <summary>
/// Supported offline-license tiers.
/// </summary>
public enum LicenseTier
{
    Lite = 0,
    Pro = 1,
    Enterprise = 2
}

/// <summary>
/// Business status of a checkout transaction.
///
/// This is different from SyncStatus:
/// TransactionStatus describes the sale itself,
/// while SyncStatus describes server synchronization.
/// </summary>
public enum TransactionStatus
{
    InProgress = 0,
    Completed = 1,
    Cancelled = 2,
    Faulted = 3
}