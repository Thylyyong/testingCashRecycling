using Microsoft.EntityFrameworkCore.Design;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Design-time-only factory the <c>dotnet ef</c> tooling uses to construct a
/// <see cref="KioskDbContext"/> without going through the app's real
/// composition root (which needs a resolved database path + a sealed
/// SQLCipher passphrase that don't exist at design time). Used by
/// <c>dotnet ef dbcontext optimize</c> to generate the AOT compiled model —
/// never referenced by the running app.
/// </summary>
public sealed class KioskDbContextFactory : IDesignTimeDbContextFactory<KioskDbContext>
{
    public KioskDbContext CreateDbContext(string[] args)
        => new("design-time.db", "design-time-placeholder-passphrase");
}
