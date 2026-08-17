using System;
using System.IO;
using System.Text.Json;
using SelfCheckoutKiosk.Core.Catalog;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class ProductCatalogTests : IDisposable
{
    private readonly string _productsJsonPath;

    public ProductCatalogTests()
    {
        _productsJsonPath = Path.Combine(AppContext.BaseDirectory, "products.json");
        if (File.Exists(_productsJsonPath))
        {
            try { File.Delete(_productsJsonPath); } catch { }
        }
    }

    public void Dispose()
    {
        if (File.Exists(_productsJsonPath))
        {
            try { File.Delete(_productsJsonPath); } catch { }
        }
    }

    [Fact]
    public void Lookup_HardcodedProducts_AreSeededCorrectly()
    {
        var catalog = new ProductCatalog();

        var coke = catalog.Lookup("8886037000185");
        Assert.Equal("Coca-Cola Original 330ml Can", coke.Description);
        Assert.Equal(0.75m, coke.UsdPrice);

        var chips = catalog.Lookup("8850718801435");
        Assert.Equal("Lay's Classic Potato Chips", chips.Description);
        Assert.Equal(1.50m, chips.UsdPrice);
    }

    [Fact]
    public void Lookup_UnknownBarcode_ReturnsGeneratedGenericFallback()
    {
        var catalog = new ProductCatalog();
        string unknownBarcode = "9999999999999";

        var product = catalog.Lookup(unknownBarcode);

        Assert.Equal(unknownBarcode, product.Ean13);
        Assert.Equal($"Scanned Item ({unknownBarcode})", product.Description);
        Assert.Equal(1.00m, product.UsdPrice);
    }

    [Fact]
    public void Lookup_CustomProductsJson_LoadsSuccessfully()
    {
        var customProducts = new[]
        {
            new { ean13 = "1111111111111", description = "Test Custom Product 1", usdPrice = 5.99m },
            new { ean13 = "2222222222222", description = "Test Custom Product 2", usdPrice = 10.50m }
        };
        string json = JsonSerializer.Serialize(customProducts);
        File.WriteAllText(_productsJsonPath, json);

        var catalog = new ProductCatalog();

        var prod1 = catalog.Lookup("1111111111111");
        Assert.Equal("Test Custom Product 1", prod1.Description);
        Assert.Equal(5.99m, prod1.UsdPrice);

        var prod2 = catalog.Lookup("2222222222222");
        Assert.Equal("Test Custom Product 2", prod2.Description);
        Assert.Equal(10.50m, prod2.UsdPrice);

        var coke = catalog.Lookup("8886037000185");
        Assert.Equal("Coca-Cola Original 330ml Can", coke.Description);
        Assert.Equal(0.75m, coke.UsdPrice);
    }
}
