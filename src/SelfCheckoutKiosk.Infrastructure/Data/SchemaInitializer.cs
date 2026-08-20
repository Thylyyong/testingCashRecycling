using System.Data;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Creates the kiosk schema WITHOUT EF's <c>EnsureCreated()</c>/<c>Migrate()</c>
/// — both are <c>RequiresDynamicCode</c>-annotated (they build/diff the model
/// at runtime) and stay unsupported under Native AOT even once a compiled
/// model is in play. Instead this replays a schema script generated ONCE, at
/// design time, from <c>context.Database.GenerateCreateScript()</c> — plain
/// ADO.NET, zero reflection, zero dynamic code.
///
/// TODO(Back-End): regenerate <c>Schema/001_InitialCreate.sql</c> (and add a
/// 002_... script alongside it, never edit 001 in place) whenever the model
/// shape changes — there is no migration-diffing safety net here by design.
/// </summary>
public static class SchemaInitializer
{
    private const string SchemaResourceName = "SelfCheckoutKiosk.Infrastructure.Data.Schema.001_InitialCreate.sql";

    /// <summary>Idempotent: no-ops if the "Products" table already exists.</summary>
    public static void EnsureCreated(SqliteConnection connection)
    {
        if (connection.State != ConnectionState.Open)
            connection.Open();

        using (SqliteCommand checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Products';";
            if (checkCommand.ExecuteScalar() is not null)
                return;
        }

        using SqliteCommand createCommand = connection.CreateCommand();
        createCommand.CommandText = ReadEmbeddedSchemaScript();
        createCommand.ExecuteNonQuery();
    }

    private static string ReadEmbeddedSchemaScript()
    {
        Assembly assembly = typeof(SchemaInitializer).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(SchemaResourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded schema resource '{SchemaResourceName}' not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
