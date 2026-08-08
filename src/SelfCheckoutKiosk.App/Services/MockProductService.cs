using System.Collections.Generic;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services
{
    public class MockProductService : IProductService
    {
        public IEnumerable<Product> GetAllProducts()
        {
            return new List<Product>
            {

                // Dairy & Eggs
                new Product { Sku = "8485234828415", Name = "Organic Fresh Milk 1L", Category = "Dairy", Price = 2.50m },
                new Product { Sku = "4111154381009", Name = "Free Range Eggs 10pk", Category = "Dairy", Price = 3.20m },
                new Product { Sku = "7072010958018", Name = "Cheddar Cheese Block 250g", Category = "Dairy", Price = 2.80m },

                // Beverages - Soft Drinks
                new Product { Sku = "7243026805126", Name = "Coca-Cola 330ml", Category = "Beverages", Price = 0.50m },
                new Product { Sku = "8975067721347", Name = "Pepsi 330ml", Category = "Beverages", Price = 0.50m },
                new Product { Sku = "6855258299058", Name = "Sprite Lemon-Lime 330ml", Category = "Beverages", Price = 0.50m },
                new Product { Sku = "4644880396783", Name = "Fanta Orange 330ml", Category = "Beverages", Price = 0.50m },

                // Beverages - Juices & Energy
                new Product { Sku = "9322386486164", Name = "Dasani Drinking Water 600ml", Category = "Beverages", Price = 0.35m },
                new Product { Sku = "3725887008914", Name = "Pocari Sweat 500ml", Category = "Beverages", Price = 1.20m },
                new Product { Sku = "2067904601298", Name = "Red Bull Energy Drink 250ml", Category = "Beverages", Price = 1.80m, IsAgeRestricted = true },
                new Product { Sku = "1456818123131", Name = "Orange Juice 1L", Category = "Beverages", Price = 2.10m },
                new Product { Sku = "8460683783843", Name = "Green Tea 500ml", Category = "Beverages", Price = 0.95m },
                new Product { Sku = "4763527319371", Name = "Iced Coffee Latte 250ml", Category = "Beverages", Price = 1.60m },

                // Fresh Produce (Weighed items)
                new Product { Sku = "7735329487883", Name = "Fuji Apples (kg)", Category = "Produce", Price = 3.50m, RequiresWeighing = true },
                new Product { Sku = "9663035162627", Name = "Bananas (kg)", Category = "Produce", Price = 1.80m, RequiresWeighing = true },

                // Alcohol (Age Restricted)
                new Product { Sku = "1449711595198", Name = "Heineken Beer 330ml", Category = "Alcohol", Price = 2.20m, IsAgeRestricted = true }

            };
        }
    }
}