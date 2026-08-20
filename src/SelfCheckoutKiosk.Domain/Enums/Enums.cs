namespace SelfCheckoutKiosk.Domain.Enums;

/// <summary>Sync state of a locally-committed transaction (Blueprint §3).</summary>
public enum SyncStatus { Pending = 0, Completed = 1 }

public enum CurrencyCode { Usd = 0, Khr = 1 }

/// <summary>Deterministic kiosk state-machine states (Blueprint §3).</summary>
public enum KioskState
{
    Idle = 0,
    Scanning = 1,
    AwaitingPayment = 2,
    ProcessingCash = 3,
    DispensingChange = 4,
    TransactionComplete = 5,
    ExactCashOnlyLockout = 6,
    Faulted = 7,
}

public enum PaymentMethod { Cash = 0, KhqrDigital = 1 }

/// <summary>Classification produced by the RegexRouter for every scan.</summary>
public enum ScanCategory { Unknown = 0, Ean13Product = 1, KhqrProfile = 2, OfflineCoupon = 3 }

/// <summary>Licensing tier bracket (Blueprint §3): Lite (max 2 kiosks, no AI),
/// Pro (max 5 kiosks, with AI), Enterprise (unlimited).</summary>
public enum LicenseTier { Lite = 0, Pro = 1, Enterprise = 2 }
