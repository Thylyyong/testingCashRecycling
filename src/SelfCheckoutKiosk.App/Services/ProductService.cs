using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.Infrastructure.Data;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Production product service backed by KioskDbContext local SQLite database,
/// with fallback to memory catalog for resilience and offline support.
/// </summary>
public class ProductService : IProductService
{
    private readonly Func<KioskDbContext>? _dbContextFactory;
    private readonly List<Product> _fallbackProducts = new();

    public ProductService(Func<KioskDbContext>? dbContextFactory = null)
    {
        _dbContextFactory = dbContextFactory;
        // Initialize default curated products in memory
        _fallbackProducts.AddRange(new MockProductService().GetAllProducts());
    }

    public IEnumerable<Product> GetAllProducts()
    {
        if (_dbContextFactory != null)
        {
            try
            {
                using var db = _dbContextFactory();
                var dbProducts = db.GetAllProducts();
                if (dbProducts.Any())
                {
                    return dbProducts.Select(p => new Product
                    {
                        Sku = p.Ean13,
                        Name = p.Description,
                        Price = p.UsdPrice,
                        Category = DetermineCategory(p.Description),
                        IsAgeRestricted = p.Description.Contains("Beer", StringComparison.OrdinalIgnoreCase) || p.Description.Contains("Red Bull", StringComparison.OrdinalIgnoreCase),
                        RequiresWeighing = p.Description.Contains("(kg)", StringComparison.OrdinalIgnoreCase)
                    }).ToList();
                }
            }
            catch
            {
                // Fallback to local memory list if database read encounters transient error
            }
        }

        return _fallbackProducts;
    }

    public Product? GetProductBySku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;

        var all = GetAllProducts();
        return all.FirstOrDefault(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));
    }

    private static string DetermineCategory(string description)
    {
        if (description.Contains("Milk", StringComparison.OrdinalIgnoreCase) || description.Contains("Eggs", StringComparison.OrdinalIgnoreCase) || description.Contains("Cheese", StringComparison.OrdinalIgnoreCase))
            return "Dairy";
        if (description.Contains("Beer", StringComparison.OrdinalIgnoreCase))
            return "Alcohol";
        if (description.Contains("Apples", StringComparison.OrdinalIgnoreCase) || description.Contains("Bananas", StringComparison.OrdinalIgnoreCase))
            return "Produce";
        return "Beverages";
    }
}
