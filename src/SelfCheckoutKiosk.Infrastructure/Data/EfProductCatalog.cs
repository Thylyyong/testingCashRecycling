using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Entities;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// EF-Core-backed <see cref="IProductCatalog"/> reading the Tailscale-synced
/// catalog out of <see cref="KioskDbContext"/>. Takes a context FACTORY, never
/// a shared instance — each lookup opens and disposes its own short-lived
/// context (Blueprint §6 composition guidance).
/// </summary>
public sealed class EfProductCatalog(Func<KioskDbContext> dbContextFactory) : IProductCatalog
{
    public Product? FindByEan13(string ean13)
    {
        using KioskDbContext db = dbContextFactory();
        return db.FindProductByEan13(ean13);
    }
}
