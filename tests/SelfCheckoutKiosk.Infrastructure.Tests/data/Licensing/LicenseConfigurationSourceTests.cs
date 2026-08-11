using Microsoft.EntityFrameworkCore;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Infrastructure.Data;
using SelfCheckoutKiosk.Infrastructure.Data.Licensing;
using Xunit;

namespace SelfCheckoutKiosk.Infrastructure.Tests.Data.Licensing;

public sealed class LicenseConfigurationSourceTests
{
    // =========================================================
    // NO LICENSE
    // =========================================================

    [Fact]
    public async Task
        LoadAsync_NoLicense_ReturnsNull()
    {
        await using var context =
            CreateContext();

        var source =
            new LicenseConfigurationSource(
                context
            );

        var result =
            await source
                .LoadAsync();

        Assert.Null(
            result
        );
    }

    // =========================================================
    // SINGLE LICENSE
    // =========================================================

    [Fact]
    public async Task
        LoadAsync_OneLicense_ReturnsLicense()
    {
        await using var context =
            CreateContext();

        var expected =
            CreateLicense(
                id: 1,
                hardwareId:
                    "KIOSK-001"
            );

        context
            .LicenseConfigurations
            .Add(
                expected
            );

        await context
            .SaveChangesAsync();

        var source =
            new LicenseConfigurationSource(
                context
            );

        var result =
            await source
                .LoadAsync();

        Assert.NotNull(
            result
        );

        Assert.Equal(
            expected.Id,
            result.Id
        );

        Assert.Equal(
            expected.HardwareId,
            result.HardwareId
        );

        Assert.Equal(
            expected.Tier,
            result.Tier
        );

        Assert.Equal(
            expected.MaxKiosks,
            result.MaxKiosks
        );

        Assert.Equal(
            expected.CashModuleEnabled,
            result.CashModuleEnabled
        );

        Assert.Equal(
            expected.AiModuleEnabled,
            result.AiModuleEnabled
        );

        Assert.Equal(
            expected.ErpSyncEnabled,
            result.ErpSyncEnabled
        );

        Assert.Equal(
            expected.ExpiresAtUtc,
            result.ExpiresAtUtc
        );

        Assert.Equal(
            expected.SignedToken,
            result.SignedToken
        );
    }

    // =========================================================
    // READ ONLY
    // =========================================================

    [Fact]
    public async Task
        LoadAsync_ReturnedLicense_IsNotTracked()
    {
        await using var context =
            CreateContext();

        context
            .LicenseConfigurations
            .Add(
                CreateLicense(
                    id: 1,
                    hardwareId:
                        "KIOSK-001"
                )
            );

        await context
            .SaveChangesAsync();

        /*
         * Remove the entity that SaveChanges left tracked.
         *
         * This ensures the assertion below measures the query
         * behavior rather than the insert operation.
         */
        context.ChangeTracker
            .Clear();

        var source =
            new LicenseConfigurationSource(
                context
            );

        var result =
            await source
                .LoadAsync();

        Assert.NotNull(
            result
        );

        Assert.Empty(
            context
                .ChangeTracker
                .Entries<
                    LicenseConfiguration
                >()
        );
    }

    // =========================================================
    // MULTIPLE LICENSES
    // =========================================================

    [Fact]
    public async Task
        LoadAsync_MultipleLicenses_Throws()
    {
        await using var context =
            CreateContext();

        context
            .LicenseConfigurations
            .AddRange(
                CreateLicense(
                    id: 1,
                    hardwareId:
                        "KIOSK-001"
                ),
                CreateLicense(
                    id: 2,
                    hardwareId:
                        "KIOSK-002"
                )
            );

        await context
            .SaveChangesAsync();

        context.ChangeTracker
            .Clear();

        var source =
            new LicenseConfigurationSource(
                context
            );

        await Assert.ThrowsAsync<
            InvalidOperationException
        >(
            () =>
                source
                    .LoadAsync()
        );
    }

    // =========================================================
    // CANCELLATION
    // =========================================================

    [Fact]
    public async Task
        LoadAsync_CancelledToken_Throws()
    {
        await using var context =
            CreateContext();

        var source =
            new LicenseConfigurationSource(
                context
            );

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource
            .Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException
        >(
            () =>
                source.LoadAsync(
                    cancellationTokenSource.Token
                )
        );
    }

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    [Fact]
    public void
        Constructor_NullDbContext_Throws()
    {
        Assert.Throws<
            ArgumentNullException
        >(
            () =>
                new LicenseConfigurationSource(
                    null!
                )
        );
    }

    // =========================================================
    // TEST DATABASE
    // =========================================================

    private static KioskDbContext
        CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<
                KioskDbContext
            >()
            .UseInMemoryDatabase(
                databaseName:
                    $"license-source-{Guid.NewGuid():N}"
            )
            .Options;

        return new KioskDbContext(
            options
        );
    }

    // =========================================================
    // TEST DATA
    // =========================================================

    private static LicenseConfiguration
        CreateLicense(
            long id,
            string hardwareId)
    {
        return new LicenseConfiguration
        {
            Id =
                id,

            HardwareId =
                hardwareId,

            Tier =
                LicenseTier.Pro,

            MaxKiosks =
                5,

            CashModuleEnabled =
                true,

            AiModuleEnabled =
                true,

            ErpSyncEnabled =
                false,

            ExpiresAtUtc =
                new DateTimeOffset(
                    2035,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero
                ),

            SignedToken =
                $"signed-token-{id}"
        };
    }
}