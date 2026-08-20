using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.Infrastructure.Sync;

public sealed record LineItemSyncDto(
    [property: JsonPropertyName("ean13")] string Ean13,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("unitPriceUsd")] decimal UnitPriceUsd,
    [property: JsonPropertyName("quantity")] int Quantity);

/// <summary>Wire shape for the Push side. Carries <c>TransactionGuid</c>
/// explicitly so the ERP can dedupe — a retried push after a dropped ack
/// must be a no-op there, not a duplicate transaction.</summary>
public sealed record TransactionSyncDto(
    [property: JsonPropertyName("transactionGuid")] Guid TransactionGuid,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("totalUsd")] decimal TotalUsd,
    [property: JsonPropertyName("tenderedUsd")] decimal TenderedUsd,
    [property: JsonPropertyName("paymentMethod")] int? PaymentMethod,
    [property: JsonPropertyName("lineItems")] IReadOnlyList<LineItemSyncDto> LineItems);

/// <summary>Wire shape for the Pull side — a single catalog master-data row
/// (new SKU or price change) from the ERP.</summary>
public sealed record ProductCatalogUpdateDto(
    [property: JsonPropertyName("ean13")] string Ean13,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("usdPrice")] decimal UsdPrice,
    [property: JsonPropertyName("khrPrice")] decimal KhrPrice);

/// <summary>
/// Transport abstraction between <see cref="TailscaleSyncWorker"/> and the
/// central ERP — kept as an interface so the worker's push/pull loop and
/// idempotency logic are testable without a live HTTP endpoint.
/// </summary>
public interface IErpSyncClient
{
    /// <summary>Returns true once the ERP has durably acknowledged the
    /// transaction (already dedupe'd there by <see cref="TransactionSyncDto.TransactionGuid"/>).</summary>
    Task<bool> PushTransactionAsync(TransactionSyncDto transaction, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductCatalogUpdateDto>> PullCatalogUpdatesAsync(CancellationToken cancellationToken);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TransactionSyncDto))]
[JsonSerializable(typeof(ProductCatalogUpdateDto[]))]
internal sealed partial class ErpSyncJsonContext : JsonSerializerContext;
