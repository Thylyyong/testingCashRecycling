using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Infrastructure.Data;
using Xunit;
using Xunit.Sdk;

namespace SelfCheckoutKiosk.Infrastructure.Tests;

/// <summary>
/// Verifies the persistence model exposed by KioskDbContext.
///
/// These tests intentionally use EF Core's in-memory provider.
/// They verify entity/model configuration without requiring the
/// production SQLCipher native provider.
/// </summary>
public sealed class KioskDbContextModelTests
{
    [Fact]
    public void Model_ContainsExpectedPersistenceEntities()
    {
        using var context =
            CreateContext();

        Assert.NotNull(
            context.Model.FindEntityType(
                typeof(Product)
            )
        );

        Assert.NotNull(
            context.Model.FindEntityType(
                typeof(Transaction)
            )
        );

        Assert.NotNull(
            context.Model.FindEntityType(
                typeof(TransactionLine)
            )
        );

        Assert.NotNull(
            context.Model.FindEntityType(
                typeof(LicenseConfiguration)
            )
        );
    }

    [Fact]
    public void Model_UsesExpectedTableNames()
    {
        using var context =
            CreateContext();

        Assert.Equal(
            "Products",
            GetEntityType<Product>(
                    context
                )
                .GetTableName()
        );

        Assert.Equal(
            "Transactions",
            GetEntityType<Transaction>(
                    context
                )
                .GetTableName()
        );

        Assert.Equal(
            "TransactionLines",
            GetEntityType<TransactionLine>(
                    context
                )
                .GetTableName()
        );

        Assert.Equal(
            "LicenseConfigurations",
            GetEntityType<LicenseConfiguration>(
                    context
                )
                .GetTableName()
        );
    }

    [Fact]
    public void Transaction_UsesTransactionGuidAsPrimaryKey()
    {
        using var context =
            CreateContext();

        var entityType =
            GetEntityType<Transaction>(
                context
            );

        var primaryKey =
            entityType.FindPrimaryKey()
            ?? throw new XunitException(
                "Transaction does not have a primary key."
            );

        var property =
            Assert.Single(
                primaryKey.Properties
            );

        Assert.Equal(
            nameof(
                Transaction.TransactionGuid
            ),
            property.Name
        );

        Assert.Equal(
            ValueGenerated.Never,
            property.ValueGenerated
        );
    }

    [Fact]
    public void Product_Ean13Index_IsUnique()
    {
        using var context =
            CreateContext();

        var entityType =
            GetEntityType<Product>(
                context
            );

        var index =
            entityType
                .GetIndexes()
                .SingleOrDefault(
                    candidate =>
                        candidate.Properties
                            .Count ==
                            1 &&
                        candidate.Properties[0]
                            .Name ==
                            nameof(
                                Product.Ean13
                            )
                );

        Assert.NotNull(
            index
        );

        Assert.True(
            index.IsUnique
        );
    }

    [Fact]
    public void Transaction_SyncStatus_IsIndexed()
    {
        using var context =
            CreateContext();

        var entityType =
            GetEntityType<Transaction>(
                context
            );

        var indexExists =
            entityType
                .GetIndexes()
                .Any(
                    index =>
                        index.Properties
                            .Count ==
                            1 &&
                        index.Properties[0]
                            .Name ==
                            nameof(
                                Transaction.SyncStatus
                            )
                );

        Assert.True(
            indexExists
        );
    }

    [Fact]
    public void Transaction_CreatedAtUtc_IsIndexed()
    {
        using var context =
            CreateContext();

        var entityType =
            GetEntityType<Transaction>(
                context
            );

        var indexExists =
            entityType
                .GetIndexes()
                .Any(
                    index =>
                        index.Properties
                            .Count ==
                            1 &&
                        index.Properties[0]
                            .Name ==
                            nameof(
                                Transaction.CreatedAtUtc
                            )
                );

        Assert.True(
            indexExists
        );
    }

    [Fact]
    public void TransactionLine_ReferencesTransactionByTransactionGuid()
    {
        using var context =
            CreateContext();

        var entityType =
            GetEntityType<TransactionLine>(
                context
            );

        var foreignKey =
            entityType
                .GetForeignKeys()
                .SingleOrDefault(
                    candidate =>
                        candidate
                            .PrincipalEntityType
                            .ClrType ==
                        typeof(Transaction)
                );

        Assert.NotNull(
            foreignKey
        );

        var foreignKeyProperty =
            Assert.Single(
                foreignKey.Properties
            );

        Assert.Equal(
            nameof(
                TransactionLine.TransactionGuid
            ),
            foreignKeyProperty.Name
        );

        Assert.Equal(
            DeleteBehavior.Cascade,
            foreignKey.DeleteBehavior
        );
    }

    [Fact]
    public void TransactionLine_ProductId_IsNotCatalogForeignKey()
    {
        using var context =
            CreateContext();

        var entityType =
            GetEntityType<TransactionLine>(
                context
            );

        var productForeignKeyExists =
            entityType
                .GetForeignKeys()
                .Any(
                    foreignKey =>
                        foreignKey
                            .PrincipalEntityType
                            .ClrType ==
                        typeof(Product)
                );

        Assert.False(
            productForeignKeyExists
        );
    }

    [Fact]
    public void Product_Ean13_HasExpectedMaximumLength()
    {
        using var context =
            CreateContext();

        var property =
            GetEntityType<Product>(
                    context
                )
                .FindProperty(
                    nameof(
                        Product.Ean13
                    )
                )
            ?? throw new XunitException(
                "Product.Ean13 was not found in the EF model."
            );

        Assert.Equal(
            13,
            property.GetMaxLength()
        );

        Assert.False(
            property.IsNullable
        );
    }

    [Fact]
    public void LicenseConfiguration_HardwareId_IsIndexed()
    {
        using var context =
            CreateContext();

        var entityType =
            GetEntityType<
                LicenseConfiguration
            >(
                context
            );

        var indexExists =
            entityType
                .GetIndexes()
                .Any(
                    index =>
                        index.Properties
                            .Count ==
                            1 &&
                        index.Properties[0]
                            .Name ==
                            nameof(
                                LicenseConfiguration
                                    .HardwareId
                            )
                );

        Assert.True(
            indexExists
        );
    }

    private static KioskDbContext
        CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<
                    KioskDbContext
                >()
                .UseInMemoryDatabase(
                    "KioskDbContextModelTests-" +
                    Guid.NewGuid()
                        .ToString(
                            "N"
                        )
                )
                .Options;

        return new KioskDbContext(
            options
        );
    }

    private static IEntityType
        GetEntityType<TEntity>(
            KioskDbContext context)
        where TEntity : class
    {
        return context.Model
                   .FindEntityType(
                       typeof(TEntity)
                   )
               ?? throw new XunitException(
                   $"Entity '{typeof(TEntity).Name}' " +
                   "was not found in the EF model."
               );
    }
}