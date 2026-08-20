using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Infrastructure.Data.CompiledModels;
using SQLitePCL;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Encrypted persistence (Blueprint §3): EF Core over SQLite + SQLCipher
/// (AES-256 at rest via the SQLCipher connection password). The passphrase is
/// injected by the caller — sealed via DPAPI/TPM at the composition root — and
/// is NEVER hard-coded here. Register this via a FACTORY delegate, not a
/// singleton; EF contexts are cheap and are not thread-safe to share.
/// </summary>
public sealed class KioskDbContext : DbContext
{
    private readonly string _databasePath;
    private readonly string _sqlCipherPassphrase;

    static KioskDbContext()
    {
        // SQLitePCLRaw needs an explicit provider registration; the
        // e_sqlcipher bundle swaps in the SQLCipher-enabled native SQLite
        // build in place of the plain e_sqlite3 one.
        Batteries_V2.Init();
    }

    // The base DbContext() constructor is unconditionally annotated
    // RequiresUnreferencedCode/RequiresDynamicCode — EF Core doesn't (yet)
    // distinguish "will build the model via reflection" from "will be handed
    // a precompiled model via UseModel()". OnConfiguring below always
    // supplies the compiled KioskDbContextModel, so the reflection path this
    // warns about is never actually reached; this is the suppression EF's own
    // Native AOT + compiled-model docs prescribe for exactly this situation.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "OnConfiguring always supplies a precompiled model via UseModel().")]
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "OnConfiguring always supplies a precompiled model via UseModel().")]
    public KioskDbContext(string databasePath, string sqlCipherPassphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlCipherPassphrase);
        _databasePath = databasePath;
        _sqlCipherPassphrase = sqlCipherPassphrase;
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<LicenseConfiguration> LicenseConfigurations => Set<LicenseConfiguration>();

    /// <summary>AOT-safe schema creation — see <see cref="SchemaInitializer"/>.
    /// Call this instead of <c>Database.EnsureCreated()</c>, which is
    /// RequiresDynamicCode-annotated and unsupported under Native AOT.</summary>
    public void EnsureSchemaCreated() => SchemaInitializer.EnsureCreated((SqliteConnection)Database.GetDbConnection());

    // Raw ADO.NET, not LINQ — deliberately. `dotnet ef dbcontext optimize
    // --precompile-queries` (EF 9, explicitly experimental) was tried against
    // this schema and fails on every query shape tested (parameterless Any(),
    // predicated Any(), FirstOrDefault()) with "Dynamic LINQ queries are not
    // supported when precompiling queries" regardless of whether the query is
    // a DbContext member or takes an external context parameter. Since
    // PublishAot=true makes EF categorically refuse ANY non-precompiled LINQ
    // query at runtime (proven: the App crashed with "Query wasn't
    // precompiled and dynamic code isn't supported (NativeAOT)" before this
    // change), these two reads bypass LINQ entirely — same proven pattern as
    // SchemaInitializer. Revisit once query precompilation matures upstream.
    public bool HasAnyProduct()
    {
        var connection = (SqliteConnection)Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Products);";
        return (long)command.ExecuteScalar()! == 1;
    }

    public Product? FindProductByEan13(string ean13)
    {
        var connection = (SqliteConnection)Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Ean13, Description, UsdPrice, KhrPrice FROM Products WHERE Ean13 = $ean13 LIMIT 1;";
        command.Parameters.AddWithValue("$ean13", ean13);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return new Product
        {
            Ean13 = reader.GetString(0),
            Description = reader.GetString(1),
            UsdPrice = reader.GetDecimal(2),
            KhrPrice = reader.GetDecimal(3),
        };
    }

    public IReadOnlyList<Product> GetAllProducts()
    {
        var connection = (SqliteConnection)Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Ean13, Description, UsdPrice, KhrPrice FROM Products;";

        using SqliteDataReader reader = command.ExecuteReader();
        var list = new List<Product>();
        while (reader.Read())
        {
            list.Add(new Product
            {
                Ean13 = reader.GetString(0),
                Description = reader.GetString(1),
                UsdPrice = reader.GetDecimal(2),
                KhrPrice = reader.GetDecimal(3),
            });
        }
        return list;
    }

    // Same raw-ADO.NET rationale as HasAnyProduct/FindProductByEan13 above —
    // TailscaleSyncWorker runs inside the (PublishAot=true) App process, so
    // its queries are subject to the same "no dynamic LINQ" runtime gate.

    /// <summary>Transactions not yet acknowledged by the ERP, with their line
    /// items. Read-only snapshot — callers persist outcomes via
    /// <see cref="MarkTransactionCompleted"/>, not by saving these back.</summary>
    public IReadOnlyList<Transaction> GetPendingTransactions()
    {
        var connection = (SqliteConnection)Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();

        // A single LEFT JOIN, not a per-transaction follow-up query keyed by
        // a re-formatted TransactionGuid string parameter. SQLite tracks
        // storage class per VALUE, not strictly per declared column type —
        // EF's own Guid writes and a hand-built TEXT parameter comparing
        // against them are not guaranteed to match byte-for-byte even though
        // the column is declared TEXT. Comparing the join columns to each
        // other (both written by EF, so both use whatever representation EF
        // actually picked) sidesteps needing to know or reproduce that
        // representation at all.
        var transactionsByGuid = new Dictionary<Guid, Transaction>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.TransactionGuid, t.CreatedAtUtc, t.TotalUsd, t.TenderedUsd, t.PaymentMethod,
                   li.Ean13, li.Description, li.UnitPriceUsd, li.Quantity
            FROM Transactions t
            LEFT JOIN LineItem li ON li.TransactionGuid = t.TransactionGuid
            WHERE t.SyncStatus = 'Pending';
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            Guid transactionGuid = reader.GetGuid(0);
            if (!transactionsByGuid.TryGetValue(transactionGuid, out Transaction? transaction))
            {
                transaction = new Transaction
                {
                    TransactionGuid = transactionGuid,
                    CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    TotalUsd = reader.GetDecimal(2),
                    TenderedUsd = reader.GetDecimal(3),
                    PaymentMethod = reader.IsDBNull(4) ? null : (PaymentMethod)reader.GetInt32(4),
                    SyncStatus = SyncStatus.Pending,
                };
                transactionsByGuid.Add(transactionGuid, transaction);
            }

            if (!reader.IsDBNull(5)) // LEFT JOIN: no line items means these columns are all NULL.
            {
                transaction.LineItems.Add(new LineItem
                {
                    Ean13 = reader.GetString(5),
                    Description = reader.GetString(6),
                    UnitPriceUsd = reader.GetDecimal(7),
                    Quantity = reader.GetInt32(8),
                });
            }
        }

        return [.. transactionsByGuid.Values];
    }

    /// <summary>Idempotent via <paramref name="transactionGuid"/> — safe to
    /// call again for a transaction the ERP already acknowledged.</summary>
    public void MarkTransactionCompleted(Guid transactionGuid)
    {
        var connection = (SqliteConnection)Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();

        // COLLATE NOCASE: EF's Guid-to-TEXT conversion stores uppercase hex
        // (confirmed empirically — "89CB67A9-...", not Guid.ToString()'s
        // lowercase), and SQLite TEXT comparison is byte-exact by default.
        // Case-insensitive comparison sidesteps needing to track that
        // convention (or a future EF version changing it) at all.
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Transactions SET SyncStatus = 'Completed' WHERE TransactionGuid = $guid COLLATE NOCASE;";
        command.Parameters.AddWithValue("$guid", transactionGuid.ToString());
        command.ExecuteNonQuery();
    }

    /// <summary>Insert-or-update by EAN-13 — the ERP catalog pull is a master
    /// -data mirror, not an append log.</summary>
    public void UpsertProduct(string ean13, string description, decimal usdPrice, decimal khrPrice)
    {
        var connection = (SqliteConnection)Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Products (Ean13, Description, UsdPrice, KhrPrice)
            VALUES ($ean13, $description, $usdPrice, $khrPrice)
            ON CONFLICT(Ean13) DO UPDATE SET
                Description = excluded.Description,
                UsdPrice = excluded.UsdPrice,
                KhrPrice = excluded.KhrPrice;
            """;
        command.Parameters.AddWithValue("$ean13", ean13);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$usdPrice", usdPrice);
        command.Parameters.AddWithValue("$khrPrice", khrPrice);
        command.ExecuteNonQuery();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Password = _sqlCipherPassphrase,
        };

        // Hand EF Core a CLOSED connection so it owns the open/close/dispose
        // lifecycle (an already-open connection is treated as externally
        // owned and EF will never close it — the file would stay locked for
        // the app's whole lifetime). Because `synchronous` is a per-connection
        // pragma (unlike `journal_mode`, which persists in the file header),
        // the raw-connection PRAGMA hook re-runs on every open, not just once.
        var connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
        connection.StateChange += (_, e) =>
        {
            if (e.CurrentState != System.Data.ConnectionState.Open) return;

            using SqliteCommand pragmaCommand = connection.CreateCommand();
            pragmaCommand.CommandText = "PRAGMA journal_mode='WAL'; PRAGMA synchronous='FULL';";
            pragmaCommand.ExecuteNonQuery();
        };

        // AOT compiled model — see Data/CompiledModels/*.cs. EF Core would
        // otherwise build+cache this model via reflection on first use, which
        // is RequiresDynamicCode-annotated and throws outright once
        // PublishAot=true is set anywhere in the graph (proven: the App
        // entry point failed at startup with "Model building is not
        // supported when publishing with NativeAOT" before this was wired
        // in). EF Core CAN auto-discover this via the
        // [assembly: DbContextModel(...)] attribute in
        // KioskDbContextAssemblyAttributes.cs alone, but UseModel() is wired
        // explicitly so the dependency is visible in code, not just codegen
        // metadata.
        //
        // TODO(Back-End): regenerate whenever the entity shapes change:
        //   dotnet ef dbcontext optimize --project src/SelfCheckoutKiosk.Infrastructure
        //     --startup-project src/SelfCheckoutKiosk.Infrastructure --context KioskDbContext
        //     --output-dir Data/CompiledModels
        //     --namespace SelfCheckoutKiosk.Infrastructure.Data.CompiledModels --nativeaot
        optionsBuilder.UseModel(KioskDbContextModel.Instance);

        optionsBuilder.UseSqlite(connection);
    }

    /// <summary>Source of truth for the model shape — but NOT what runs at
    /// runtime once <see cref="OnConfiguring"/> calls <c>UseModel()</c>. The
    /// compiled model baked this configuration in at generation time; editing
    /// this method has NO runtime effect until you rerun
    /// <c>dotnet ef dbcontext optimize</c> to refresh Data/CompiledModels.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.TransactionGuid);
            entity.Property(t => t.SyncStatus).HasConversion<string>();

            entity.OwnsMany(t => t.LineItems, lineItems =>
            {
                lineItems.WithOwner().HasForeignKey("TransactionGuid");
                lineItems.Property<int>("Id");
                lineItems.HasKey("Id");
            });
        });

        modelBuilder.Entity<Product>(entity => entity.HasKey(p => p.Ean13));

        modelBuilder.Entity<LicenseConfiguration>(entity =>
        {
            entity.HasKey(l => l.HardwareId);
            entity.Property(l => l.Tier).HasConversion<string>();
        });
    }
}
