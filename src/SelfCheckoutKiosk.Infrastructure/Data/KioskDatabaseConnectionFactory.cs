using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Creates fully initialized local kiosk database connections.
///
/// Initialization order is security-sensitive:
///
/// 1. Open the SQLCipher-backed SQLite connection.
/// 2. Apply the database encryption key.
/// 3. Verify that SQLCipher is actually present.
/// 4. Verify that the supplied key can read the database.
/// 5. Enable WAL.
/// 6. Enforce synchronous=FULL.
///
/// The connection is returned already open so EF Core cannot
/// access the database before these initialization steps finish.
///
/// This class does not obtain or persist the encryption key.
/// The caller must provide key material from the kiosk's secure
/// secret-storage boundary.
/// </summary>
public sealed class KioskDatabaseConnectionFactory
{
    /*
     * This project deliberately references SQLitePCLRaw.provider.winsqlite3
     * directly rather than a SQLitePCLRaw.bundle_* package (see
     * SelfCheckoutKiosk.Infrastructure.csproj comment on Sqlite.Core) so the
     * native SQLCipher provider can be swapped in later without pulling in
     * plain SQLite. The bundle packages carry SQLitePCLRaw.batteries_v2,
     * which auto-registers a provider via Batteries_v2.Init() — that package
     * is NOT referenced here, so Microsoft.Data.Sqlite has no provider
     * registered until we set one explicitly. Without this, the first
     * SqliteConnection touches SQLitePCL.raw's static state uninitialized
     * and throws a TypeInitializationException that unwinds past any normal
     * try/catch because it happens inside a type initializer, not inside
     * this method's own logic.
     *
     * A static constructor runs exactly once, guaranteed before any other
     * member of this type is touched — including the first
     * CreateOpenConnection call — so this fixes the ordering regardless of
     * caller. SetProvider takes a concrete, directly-referenced type
     * (no reflection, no assembly scanning), so it is not a trimming/AOT
     * risk: the trimmer only removes types nothing reaches, and this type
     * is always reached because CreateOpenConnection constructs a
     * SqliteConnection every time it runs.
     */
    static KioskDatabaseConnectionFactory()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
    }

    /// <summary>
    /// Creates and opens a hardened SQLCipher connection.
    /// </summary>
    public SqliteConnection CreateOpenConnection(
        string databasePath,
        string encryptionKey)
    {
        if (
            string.IsNullOrWhiteSpace(
                databasePath
            )
        )
        {
            throw new ArgumentException(
                "The kiosk database path is required.",
                nameof(databasePath)
            );
        }

        if (
            string.IsNullOrWhiteSpace(
                encryptionKey
            )
        )
        {
            throw new ArgumentException(
                "The kiosk database encryption key is required.",
                nameof(encryptionKey)
            );
        }

        var normalizedDatabasePath =
            Path.GetFullPath(
                databasePath
            );

        EnsureDatabaseDirectoryExists(
            normalizedDatabasePath
        );

        var connectionString =
            BuildConnectionString(
                normalizedDatabasePath,
                encryptionKey
            );

        var connection =
            new SqliteConnection(
                connectionString
            );

        try
        {
            /*
             * Microsoft.Data.Sqlite sends PRAGMA key immediately
             * after opening when Password is present.
             *
             * This requires an encryption-capable native SQLite
             * implementation such as SQLCipher.
             */
            connection.Open();

            VerifySqlCipher(
                connection
            );

            VerifyDatabaseKey(
                connection
            );

            ConfigureDurability(
                connection
            );

            VerifyDurability(
                connection
            );

            return connection;
        }
        catch
        {
            connection.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Builds the SQLite connection string without exposing the
    /// encryption key through manual string concatenation.
    /// </summary>
    private static string BuildConnectionString(
        string databasePath,
        string encryptionKey)
    {
        var builder =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    databasePath,

                Mode =
                    SqliteOpenMode
                        .ReadWriteCreate,

                /*
                 * Do not use Cache=Shared with WAL.
                 *
                 * Microsoft specifically discourages combining
                 * SQLite shared-cache mode with WAL.
                 */
                Cache =
                    SqliteCacheMode
                        .Default,

                /*
                 * Each context receives its own explicitly
                 * initialized physical connection.
                 *
                 * This avoids accidentally obtaining a pooled
                 * connection whose connection-local PRAGMAs were
                 * configured by another lifecycle.
                 */
                Pooling =
                    false,

                /*
                 * Microsoft.Data.Sqlite applies this as
                 * PRAGMA key immediately after connection open
                 * when the native SQLite implementation supports
                 * encryption.
                 */
                Password =
                    encryptionKey,

                /*
                 * Ensure relational FK constraints are enforced.
                 */
                ForeignKeys =
                    true
            };

        return builder
            .ToString();
    }

    /// <summary>
    /// Fails closed when the active native SQLite library is not
    /// SQLCipher.
    ///
    /// Plain SQLite must never be accepted silently because the
    /// Password connection-string property has no encryption
    /// effect when the native library lacks encryption support.
    /// </summary>
    private static void VerifySqlCipher(
        SqliteConnection connection)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA cipher_version;";

        var result =
            command.ExecuteScalar();

        var cipherVersion =
            Convert.ToString(
                result,
                CultureInfo.InvariantCulture
            );

        if (
            string.IsNullOrWhiteSpace(
                cipherVersion
            )
        )
        {
            throw new InvalidOperationException(
                "The active SQLite native library is not " +
                "SQLCipher. Encrypted kiosk persistence cannot " +
                "be started."
            );
        }
    }

    /// <summary>
    /// Forces an actual encrypted database read.
    ///
    /// A wrong SQLCipher key normally fails when database pages
    /// are first read, not merely when PRAGMA key is issued.
    /// </summary>
    private static void VerifyDatabaseKey(
        SqliteConnection connection)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            "SELECT count(*) FROM sqlite_master;";

        _ =
            command.ExecuteScalar();
    }

    /// <summary>
    /// Applies the durability settings required by the kiosk
    /// architecture.
    /// </summary>
    private static void ConfigureDurability(
        SqliteConnection connection)
    {
        using (
            var walCommand =
                connection.CreateCommand()
        )
        {
            walCommand.CommandText =
                "PRAGMA journal_mode=WAL;";

            var result =
                walCommand.ExecuteScalar();

            var journalMode =
                Convert.ToString(
                    result,
                    CultureInfo.InvariantCulture
                );

            if (
                !string.Equals(
                    journalMode,
                    "wal",
                    StringComparison
                        .OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidOperationException(
                    "SQLite refused to enable WAL journal mode."
                );
            }
        }

        using (
            var synchronousCommand =
                connection.CreateCommand()
        )
        {
            synchronousCommand.CommandText =
                "PRAGMA synchronous=FULL;";

            synchronousCommand
                .ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Reads the active PRAGMA values back from SQLite rather
    /// than assuming the requested settings were accepted.
    /// </summary>
    private static void VerifyDurability(
        SqliteConnection connection)
    {
        using (
            var journalCommand =
                connection.CreateCommand()
        )
        {
            journalCommand.CommandText =
                "PRAGMA journal_mode;";

            var journalMode =
                Convert.ToString(
                    journalCommand.ExecuteScalar(),
                    CultureInfo.InvariantCulture
                );

            if (
                !string.Equals(
                    journalMode,
                    "wal",
                    StringComparison
                        .OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidOperationException(
                    "The kiosk database is not running in WAL " +
                    "journal mode."
                );
            }
        }

        using (
            var synchronousCommand =
                connection.CreateCommand()
        )
        {
            synchronousCommand.CommandText =
                "PRAGMA synchronous;";

            var rawValue =
                synchronousCommand
                    .ExecuteScalar();

            var synchronousValue =
                Convert.ToInt32(
                    rawValue,
                    CultureInfo.InvariantCulture
                );

            /*
             * SQLite synchronous values:
             *
             * 0 = OFF
             * 1 = NORMAL
             * 2 = FULL
             * 3 = EXTRA
             */
            if (synchronousValue != 2)
            {
                throw new InvalidOperationException(
                    "The kiosk database is not configured with " +
                    "PRAGMA synchronous=FULL."
                );
            }
        }
    }

    private static void
        EnsureDatabaseDirectoryExists(
            string databasePath)
    {
        var directoryPath =
            Path.GetDirectoryName(
                databasePath
            );

        if (
            string.IsNullOrWhiteSpace(
                directoryPath
            )
        )
        {
            return;
        }

        Directory.CreateDirectory(
            directoryPath
        );
    }
}