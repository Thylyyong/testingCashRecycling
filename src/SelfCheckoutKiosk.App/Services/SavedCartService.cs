using SelfCheckoutKiosk.App.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace SelfCheckoutKiosk.App.Services
{
    public class SavedCartRecord
    {
        public string PinCode { get; set; } = string.Empty;
        public List<SavedCartItemRecord> Items { get; set; } = new();
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(2);
    }

    public class SavedCartItemRecord
    {
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    public class SavedCartService
    {
        public static SavedCartService Instance { get; } = new();

        private readonly ConcurrentDictionary<string, SavedCartRecord> _savedCarts = new();

        public string SaveCart(IEnumerable<CartItem> items)
        {
            CleanupExpired();

            var itemList = items.Select(i => new SavedCartItemRecord
            {
                Name = i.Name,
                Sku = i.Sku,
                Price = i.UnitPrice,
                Quantity = i.Quantity
            }).ToList();

            if (itemList.Count == 0)
                throw new InvalidOperationException("Cannot save an empty cart.");

            string pin;
            do
            {
                pin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            } while (_savedCarts.ContainsKey(pin));

            var record = new SavedCartRecord
            {
                PinCode = pin,
                Items = itemList,
                SavedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(2)
            };

            _savedCarts[pin] = record;
            return pin;
        }

        public bool TryRecallCart(string pin, out List<SavedCartItemRecord>? items, out string errorMessage)
        {
            CleanupExpired();
            items = null;

            if (string.IsNullOrWhiteSpace(pin) || pin.Trim().Length != 6)
            {
                errorMessage = "Please enter a valid 6-digit PIN.";
                return false;
            }

            string cleanPin = pin.Trim();

            if (!_savedCarts.TryGetValue(cleanPin, out var record))
            {
                errorMessage = "No saved cart found for this PIN. Please check and try again.";
                return false;
            }

            if (DateTime.UtcNow > record.ExpiresAt)
            {
                _savedCarts.TryRemove(cleanPin, out _);
                errorMessage = "This saved cart has expired (2-hour limit).";
                return false;
            }

            // Remove upon successful recall
            _savedCarts.TryRemove(cleanPin, out _);
            items = record.Items;
            errorMessage = string.Empty;
            return true;
        }

        public void CleanupExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _savedCarts)
            {
                if (now > kvp.Value.ExpiresAt)
                {
                    _savedCarts.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
