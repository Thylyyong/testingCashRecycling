using SelfCheckoutKiosk.Domain.Entities;

namespace SelfCheckoutKiosk.Core.Abstractions;

/// <summary>
/// Port for EAN-13 -> <see cref="Product"/> lookups (Blueprint §3). The
/// production implementation is EF-Core-backed (Infrastructure, reading the
/// synced catalog out of <c>KioskDbContext</c>) — Core depends only on this
/// abstraction so the engine never touches persistence directly.
/// </summary>
public interface IProductCatalog
{
    Product? FindByEan13(string ean13);
}
