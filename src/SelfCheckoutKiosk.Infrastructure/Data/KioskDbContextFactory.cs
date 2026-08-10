using System;
using Microsoft.EntityFrameworkCore;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Creates short-lived KioskDbContext instances backed by an
/// already-open and hardened SQLCipher connection.
///
/// KioskDbContext is intentionally created per unit of work and
/// must not be retained as a process-wide singleton.
/// </summary>
public sealed class KioskDbContextFactory
{
    private readonly KioskDatabaseConnectionFactory
        _connectionFactory;

    public KioskDbContextFactory(
        KioskDatabaseConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(
            connectionFactory
        );

        _connectionFactory =
            connectionFactory;
    }

    /// <summary>
    /// Creates a new database context.
    ///
    /// The returned context owns the underlying connection and
    /// will dispose it when the context itself is disposed.
    /// </summary>
    public KioskDbContext Create(
        string databasePath,
        string encryptionKey)
    {
        var connection =
            _connectionFactory
                .CreateOpenConnection(
                    databasePath,
                    encryptionKey
                );

        try
        {
            var options =
                new DbContextOptionsBuilder<
                        KioskDbContext
                    >()
                    .UseSqlite(
                        connection,
                        contextOwnsConnection:
                            true
                    )
                    .Options;

            return new KioskDbContext(
                options
            );
        }
        catch
        {
            connection.Dispose();

            throw;
        }
    }
}