using System;
using SelfCheckoutKiosk.Infrastructure.Data;
using Xunit;

namespace SelfCheckoutKiosk.Infrastructure.Tests;

/// <summary>
/// Verifies validation that occurs before opening the physical
/// SQLCipher database connection.
///
/// Real SQLCipher encryption and PRAGMA verification are covered
/// later when the native SQLCipher provider is available.
/// </summary>
public sealed class KioskDatabaseConnectionFactoryTests
{
    [Fact]
    public void CreateOpenConnection_EmptyDatabasePath_Throws()
    {
        var factory =
            new KioskDatabaseConnectionFactory();

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    factory.CreateOpenConnection(
                        string.Empty,
                        "test-encryption-key"
                    )
            );

        Assert.Equal(
            "databasePath",
            exception.ParamName
        );
    }

    [Fact]
    public void CreateOpenConnection_WhitespaceDatabasePath_Throws()
    {
        var factory =
            new KioskDatabaseConnectionFactory();

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    factory.CreateOpenConnection(
                        "   ",
                        "test-encryption-key"
                    )
            );

        Assert.Equal(
            "databasePath",
            exception.ParamName
        );
    }

    [Fact]
    public void CreateOpenConnection_EmptyEncryptionKey_Throws()
    {
        var factory =
            new KioskDatabaseConnectionFactory();

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    factory.CreateOpenConnection(
                        "kiosk.db",
                        string.Empty
                    )
            );

        Assert.Equal(
            "encryptionKey",
            exception.ParamName
        );
    }

    [Fact]
    public void CreateOpenConnection_WhitespaceEncryptionKey_Throws()
    {
        var factory =
            new KioskDatabaseConnectionFactory();

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    factory.CreateOpenConnection(
                        "kiosk.db",
                        "   "
                    )
            );

        Assert.Equal(
            "encryptionKey",
            exception.ParamName
        );
    }
}