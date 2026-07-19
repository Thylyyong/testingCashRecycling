namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// STUB — encrypted persistence (Blueprint §3). Real implementation is EF Core
/// over SQLite + SQLCipher (AES-256), with an OnConfiguring raw-connection hook
/// issuing PRAGMA journal_mode=WAL and PRAGMA synchronous=FULL before EF touches
/// anything. The Lead injects the SQLCipher passphrase (sealed via DPAPI/TPM) —
/// it is never hard-coded here.
///
/// TODO(Back-End): add Microsoft.EntityFrameworkCore.Sqlite + SQLitePCLRaw
/// SQLCipher bundle; derive from DbContext; add DbSet&lt;Transaction/Product/
/// LicenseConfiguration&gt;; register via a FACTORY delegate (not a singleton).
/// </summary>
public sealed class KioskDbContext
{
    // Placeholder so the graph compiles pre-EF. Replace with a real DbContext.
}
