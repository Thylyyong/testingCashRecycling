using Microsoft.EntityFrameworkCore;
using SelfCheckoutKiosk.Domain.Entities;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Local kiosk persistence context.
///
/// The context owns the kiosk's local transactional database.
///
/// SQLCipher native-provider initialization and database
/// connection hardening are configured separately so that
/// encryption secrets are never hard-coded in this class.
/// </summary>
public sealed class KioskDbContext
    : DbContext
{
    public KioskDbContext(
        DbContextOptions<KioskDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Products available to the kiosk.
    /// </summary>
    public DbSet<Product> Products =>
        Set<Product>();

    /// <summary>
    /// Locally committed checkout transactions.
    /// </summary>
    public DbSet<Transaction> Transactions =>
        Set<Transaction>();

    /// <summary>
    /// Purchased product lines belonging to transactions.
    /// </summary>
    public DbSet<TransactionLine> TransactionLines =>
        Set<TransactionLine>();

    /// <summary>
    /// Persisted offline-license configuration.
    /// </summary>
    public DbSet<LicenseConfiguration> LicenseConfigurations =>
        Set<LicenseConfiguration>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(
            modelBuilder
        );

        ConfigureProduct(
            modelBuilder
        );

        ConfigureTransaction(
            modelBuilder
        );

        ConfigureTransactionLine(
            modelBuilder
        );

        ConfigureLicenseConfiguration(
            modelBuilder
        );

        base.OnModelCreating(
            modelBuilder
        );
    }

    private static void ConfigureProduct(
        ModelBuilder modelBuilder)
    {
        var entity =
            modelBuilder.Entity<Product>();

        entity.ToTable(
            "Products"
        );

        entity.HasKey(
            product =>
                product.Id
        );

        entity.Property(
                product =>
                    product.Id
            )
            .ValueGeneratedOnAdd();

        entity.Property(
                product =>
                    product.Ean13
            )
            .IsRequired()
            .HasMaxLength(
                13
            );

        entity.HasIndex(
                product =>
                    product.Ean13
            )
            .IsUnique();

        entity.Property(
                product =>
                    product.Description
            )
            .IsRequired()
            .HasMaxLength(
                500
            );

        entity.Property(
                product =>
                    product.UsdPrice
            )
            .IsRequired();

        entity.Property(
                product =>
                    product.IsActive
            )
            .IsRequired();
    }

    private static void ConfigureTransaction(
        ModelBuilder modelBuilder)
    {
        var entity =
            modelBuilder.Entity<Transaction>();

        entity.ToTable(
            "Transactions"
        );

        /*
         * TransactionGuid is the durable transaction identity.
         *
         * It is also the idempotency key used by background
         * synchronization.
         */
        entity.HasKey(
            transaction =>
                transaction.TransactionGuid
        );

        entity.Property(
                transaction =>
                    transaction.TransactionGuid
            )
            .ValueGeneratedNever();

        entity.Property(
                transaction =>
                    transaction.CreatedAtUtc
            )
            .IsRequired();

        entity.Property(
            transaction =>
                transaction.CompletedAtUtc
        );

        entity.Property(
                transaction =>
                    transaction.TotalUsd
            )
            .IsRequired();

        entity.Property(
                transaction =>
                    transaction.TenderedUsd
            )
            .IsRequired();

        entity.Property(
                transaction =>
                    transaction.ChangeUsd
            )
            .IsRequired();

        entity.Property(
                transaction =>
                    transaction.ChangeKhr
            )
            .IsRequired();

        entity.Property(
                transaction =>
                    transaction.ExchangeRateKhrPerUsd
            )
            .IsRequired();

        entity.Property(
                transaction =>
                    transaction.Status
            )
            .IsRequired();

        entity.Property(
                transaction =>
                    transaction.SyncStatus
            )
            .IsRequired();

        /*
         * The sync worker repeatedly queries Pending rows.
         */
        entity.HasIndex(
            transaction =>
                transaction.SyncStatus
        );

        entity.HasIndex(
            transaction =>
                transaction.CreatedAtUtc
        );
    }

    private static void ConfigureTransactionLine(
        ModelBuilder modelBuilder)
    {
        var entity =
            modelBuilder.Entity<TransactionLine>();

        entity.ToTable(
            "TransactionLines"
        );

        entity.HasKey(
            line =>
                line.Id
        );

        entity.Property(
                line =>
                    line.Id
            )
            .ValueGeneratedOnAdd();

        entity.Property(
                line =>
                    line.TransactionGuid
            )
            .IsRequired();

        entity.Property(
                line =>
                    line.ProductId
            )
            .IsRequired();

        entity.Property(
                line =>
                    line.DescriptionSnapshot
            )
            .IsRequired()
            .HasMaxLength(
                500
            );

        entity.Property(
                line =>
                    line.UnitPriceUsdSnapshot
            )
            .IsRequired();

        entity.Property(
                line =>
                    line.Quantity
            )
            .IsRequired();

        entity.Property(
                line =>
                    line.LineTotalUsd
            )
            .IsRequired();

        /*
         * TransactionLine uses TransactionGuid as its foreign
         * key so it matches the transaction's stable idempotency
         * identifier.
         */
        entity.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(
                line =>
                    line.TransactionGuid
            )
            .OnDelete(
                DeleteBehavior.Cascade
            );

        /*
         * ProductId intentionally remains a scalar reference.
         *
         * Transaction history is preserved by snapshot fields and
         * should not depend on the current catalog row continuing
         * to exist.
         */
        entity.HasIndex(
            line =>
                line.TransactionGuid
        );

        entity.HasIndex(
            line =>
                line.ProductId
        );
    }

    private static void ConfigureLicenseConfiguration(
        ModelBuilder modelBuilder)
    {
        var entity =
            modelBuilder.Entity<LicenseConfiguration>();

        entity.ToTable(
            "LicenseConfigurations"
        );

        entity.HasKey(
            license =>
                license.Id
        );

        entity.Property(
                license =>
                    license.Id
            )
            .ValueGeneratedOnAdd();

        entity.Property(
                license =>
                    license.HardwareId
            )
            .IsRequired()
            .HasMaxLength(
                512
            );

        entity.Property(
                license =>
                    license.Tier
            )
            .IsRequired();

        entity.Property(
                license =>
                    license.MaxKiosks
            )
            .IsRequired();

        entity.Property(
                license =>
                    license.CashModuleEnabled
            )
            .IsRequired();

        entity.Property(
                license =>
                    license.AiModuleEnabled
            )
            .IsRequired();

        entity.Property(
                license =>
                    license.ErpSyncEnabled
            )
            .IsRequired();

        entity.Property(
                license =>
                    license.ExpiresAtUtc
            )
            .IsRequired();

        entity.Property(
                license =>
                    license.SignedToken
            )
            .IsRequired();

        entity.HasIndex(
            license =>
                license.HardwareId
        );
    }
}