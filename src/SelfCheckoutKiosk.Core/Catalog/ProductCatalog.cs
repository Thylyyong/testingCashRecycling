using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SelfCheckoutKiosk.Domain.Entities;

namespace SelfCheckoutKiosk.Core.Catalog;

/// <summary>
/// In-memory product catalog with JSON persistence support.
/// Resolves scanned barcodes (EAN-13 / UPC) to product details and prices.
/// </summary>
public sealed class ProductCatalog
{
    private readonly Dictionary<string, Product> _products = new(StringComparer.OrdinalIgnoreCase);

    public ProductCatalog()
    {
        // Seed default sample inventory (common retail barcodes)
        SeedDefaultProducts();

        // Attempt to load custom products.json if present
        TryLoadCustomProducts();
    }

    private void SeedDefaultProducts()
    {
        AddOrUpdate(new Product { Ean13 = "8886037000185", Description = "Coca-Cola Original 330ml Can", UsdPrice = 0.75m });
        AddOrUpdate(new Product { Ean13 = "8848858120011", Description = "Angkor Beer Can 330ml",        UsdPrice = 1.25m });
        AddOrUpdate(new Product { Ean13 = "8850718801435", Description = "Lay's Classic Potato Chips",     UsdPrice = 1.50m });
        AddOrUpdate(new Product { Ean13 = "8846015180021", Description = "Dasani Mineral Water 500ml",   UsdPrice = 0.50m });
        AddOrUpdate(new Product { Ean13 = "9002490100070", Description = "Red Bull Energy Drink 250ml",    UsdPrice = 1.00m });
        AddOrUpdate(new Product { Ean13 = "5711953026096", Description = "Starbucks Iced Mocha 280ml",     UsdPrice = 2.50m });
        AddOrUpdate(new Product { Ean13 = "7622210449283", Description = "Oreo Vanilla Cookies 133g",      UsdPrice = 1.20m });
        AddOrUpdate(new Product { Ean13 = "8992753211119", Description = "Indomie Mi Goreng Instant",     UsdPrice = 0.60m });
        AddOrUpdate(new Product { Ean13 = "000000000001",   Description = "Sample Item ($1.00)",            UsdPrice = 1.00m });
        AddOrUpdate(new Product { Ean13 = "000000000002",   Description = "Sample Item ($2.00)",            UsdPrice = 2.00m });
    }

    public void AddOrUpdate(Product product)
    {
        _products[product.Ean13] = product;
    }

    /// <summary>
    /// Finds product by barcode. If not found, generates a standard generic product
    /// so the kiosk can still proceed with checkout without crashing.
    /// </summary>
    public Product Lookup(string barcode)
    {
        string cleaned = barcode.Trim();

        if (_products.TryGetValue(cleaned, out var existing))
            return existing;

        // Auto-generate generic item for unlisted barcode (default $1.00)
        var fallback = new Product
        {
            Ean13 = cleaned,
            Description = $"Scanned Item ({cleaned})",
            UsdPrice = 1.00m
        };

        _products[cleaned] = fallback;
        return fallback;
    }

    public IReadOnlyCollection<Product> GetAllProducts() => _products.Values;

    private void TryLoadCustomProducts()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "products.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        string ean = item.GetProperty("ean13").GetString() ?? "";
                        string desc = item.GetProperty("description").GetString() ?? "";
                        decimal price = item.GetProperty("usdPrice").GetDecimal();

                        if (!string.IsNullOrEmpty(ean))
                        {
                            AddOrUpdate(new Product { Ean13 = ean, Description = desc, UsdPrice = price });
                        }
                    }
                }
            }
        }
        catch { /* Fall back to default seeded products */ }
    }
}
