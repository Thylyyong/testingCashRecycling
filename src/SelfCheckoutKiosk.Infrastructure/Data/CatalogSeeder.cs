using SelfCheckoutKiosk.Domain.Entities;

namespace SelfCheckoutKiosk.Infrastructure.Data;

/// <summary>
/// Sprint-1 test catalog: a handful of real-shaped products (valid EAN-13
/// check digits, USD ledger price + display-only KHR price at the kiosk's
/// ~4,100 KHR/USD reference rate) so the hardware and UI teams have something
/// to scan against before the real Tailscale-synced catalog exists.
///
/// TODO(Back-End): delete this once <c>TailscaleSyncWorker</c> populates
/// <see cref="KioskDbContext.Products"/> from the back-office catalog for real.
/// </summary>
public static class CatalogSeeder
{
    private static readonly Product[] SeedProducts =
    [
        new() { Ean13 = "8485234828415", Description = "Organic Fresh Milk 1L", UsdPrice = 2.50m, KhrPrice = 10250m },
        new() { Ean13 = "4111154381009", Description = "Free Range Eggs 10pk", UsdPrice = 3.20m, KhrPrice = 13120m },
        new() { Ean13 = "7072010958018", Description = "Cheddar Cheese Block 250g", UsdPrice = 2.80m, KhrPrice = 11480m },
        new() { Ean13 = "7243026805126", Description = "Coca-Cola 330ml", UsdPrice = 0.50m, KhrPrice = 2050m },
        new() { Ean13 = "8975067721347", Description = "Pepsi 330ml", UsdPrice = 0.50m, KhrPrice = 2050m },
        new() { Ean13 = "6855258299058", Description = "Sprite Lemon-Lime 330ml", UsdPrice = 0.50m, KhrPrice = 2050m },
        new() { Ean13 = "4644880396783", Description = "Fanta Orange 330ml", UsdPrice = 0.50m, KhrPrice = 2050m },
        new() { Ean13 = "9322386486164", Description = "Dasani Drinking Water 600ml", UsdPrice = 0.35m, KhrPrice = 1435m },
        new() { Ean13 = "3725887008914", Description = "Pocari Sweat 500ml", UsdPrice = 1.20m, KhrPrice = 4920m },
        new() { Ean13 = "2067904601298", Description = "Red Bull Energy Drink 250ml", UsdPrice = 1.80m, KhrPrice = 7380m },
        new() { Ean13 = "1456818123131", Description = "Orange Juice 1L", UsdPrice = 2.10m, KhrPrice = 8610m },
        new() { Ean13 = "8460683783843", Description = "Green Tea 500ml", UsdPrice = 0.95m, KhrPrice = 3895m },
        new() { Ean13 = "4763527319371", Description = "Iced Coffee Latte 250ml", UsdPrice = 1.60m, KhrPrice = 6560m },
        new() { Ean13 = "7735329487883", Description = "Fuji Apples (kg)", UsdPrice = 3.50m, KhrPrice = 14350m },
        new() { Ean13 = "9663035162627", Description = "Bananas (kg)", UsdPrice = 1.80m, KhrPrice = 7380m },
        new() { Ean13 = "1449711595198", Description = "Heineken Beer 330ml", UsdPrice = 2.20m, KhrPrice = 9020m },
        new() { Ean13 = "8850001100018", Description = "Coca-Cola 330ml Can", UsdPrice = 0.75m, KhrPrice = 3000m },
        new() { Ean13 = "8850124005010", Description = "Instant Noodles Pack", UsdPrice = 0.50m, KhrPrice = 2000m },
        new() { Ean13 = "4801981102901", Description = "Bottled Water 500ml", UsdPrice = 0.35m, KhrPrice = 1400m },
        new() { Ean13 = "8858899101012", Description = "Jasmine Rice 1kg Bag", UsdPrice = 1.20m, KhrPrice = 4900m },
    ];

    /// <summary>Idempotent — a no-op once the catalog has at least one row, so
    /// it's safe to call on every startup rather than gating it behind a
    /// first-run flag.</summary>
    public static void SeedIfEmpty(KioskDbContext context)
    {
        if (context.HasAnyProduct()) return;

        context.Products.AddRange(SeedProducts);
        context.SaveChanges();
    }
}
