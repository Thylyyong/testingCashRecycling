using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Initializes the kiosk's local persistence database.
///
/// The initializer deliberately does not:
///
/// - select a native SQLite provider;
/// - obtain the encryption key;
/// - store the encryption key;
/// - initialize SQLCipher licensing.
///
/// Those responsibilities belong to the executable/startup
/// integration boundary.
///
/// This class only works through KioskDbContextFactory, ensuring
/// the database connection has already passed the SQLCipher,
/// key, WAL, and synchronous=FULL checks before EF migrations
/// are applied.
/// </summary>
public sealed class KioskDatabaseInitializer
{
    private readonly KioskDbContextFactory
        _contextFactory;

    public KioskDatabaseInitializer(
        KioskDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(
            contextFactory
        );

        _contextFactory =
            contextFactory;
    }

    /// <summary>
    /// Opens the encrypted kiosk database through the hardened
    /// connection path and applies all pending EF Core migrations.
    ///
    /// This method must only be called after the executable has
    /// initialized the SQLCipher SQLitePCLRaw provider.
    /// </summary>
    public async Task InitializeAsync(
        string databasePath,
        string encryptionKey,
        CancellationToken cancellationToken =
            default)
    {
        /*
         * Do not create a separate raw SQLite connection here.
         *
         * KioskDbContextFactory ultimately goes through
         * KioskDatabaseConnectionFactory, which enforces:
         *
         *   SQLCipher
         *       ↓
         *   encryption key verification
         *       ↓
         *   WAL
         *       ↓
         *   synchronous=FULL
         *
         * before EF receives the connection.
         */
        await using var context =
            _contextFactory.Create(
                databasePath,
                encryptionKey
            );

        /*
         * Apply the migration using the same already-open,
         * encrypted and hardened connection.
         *
         * This creates a new encrypted database when necessary
         * and upgrades an existing encrypted database when new
         * migrations are introduced.
         */
        await context.Database
            .MigrateAsync(
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}