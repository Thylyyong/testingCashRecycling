using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Creates KioskDbContext instances only for EF Core
/// design-time tooling.
///
/// This factory is used by commands such as:
///
/// - dotnet ef migrations add
/// - dotnet ef migrations list
/// - dotnet ef migrations script
///
/// It is not used by the running kiosk.
///
/// Production persistence continues to use the SQLCipher-backed
/// connection path through KioskDatabaseConnectionFactory.
/// </summary>
public sealed class DesignTimeKioskDbContextFactory
    : IDesignTimeDbContextFactory<KioskDbContext>
{
    /*
     * EF Core SQLite requires a SQLitePCLRaw provider.
     *
     * Production will eventually use the SQLCipher provider.
     *
     * Migration tooling does not need encryption or access to the
     * production kiosk database, so design time uses Windows'
     * built-in SQLite implementation instead.
     *
     * The static constructor executes once per dotnet-ef process.
     */
    static DesignTimeKioskDbContextFactory()
    {
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_winsqlite3()
        );
    }

    /// <summary>
    /// Creates a relational SQLite context for migration
    /// scaffolding.
    ///
    /// An in-memory SQLite datasource prevents migration tooling
    /// from creating an accidental kiosk-design-time.db file.
    /// </summary>
    public KioskDbContext CreateDbContext(
        string[] args)
    {
        _ = args;

        var options =
            new DbContextOptionsBuilder<
                    KioskDbContext
                >()
                .UseSqlite(
                    "Data Source=:memory:"
                )
                .Options;

        return new KioskDbContext(
            options
        );
    }
}