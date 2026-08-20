using System.Collections.Generic;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services;

public interface IProductService
{
    IEnumerable<Product> GetAllProducts();
    Product? GetProductBySku(string sku);
}