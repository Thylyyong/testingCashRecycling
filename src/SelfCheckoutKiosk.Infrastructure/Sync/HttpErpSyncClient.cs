using System.Net.Http.Json;

namespace SelfCheckoutKiosk.Infrastructure.Sync;

/// <summary>
/// HTTP implementation of <see cref="IErpSyncClient"/>. Expects to run
/// entirely inside the Tailscale overlay (Blueprint §4) — the ERP endpoint is
/// never exposed on a public port, and this type does nothing to authenticate
/// beyond what the tailnet's own zero-trust ACLs already enforce. Uses the
/// source-generated <see cref="ErpSyncJsonContext"/> for AOT/trim-safe JSON
/// (no reflection-based serialization).
/// </summary>
public sealed class HttpErpSyncClient(HttpClient httpClient) : IErpSyncClient
{
    public async Task<bool> PushTransactionAsync(TransactionSyncDto transaction, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient
            .PostAsJsonAsync("api/transactions", transaction, ErpSyncJsonContext.Default.TransactionSyncDto, cancellationToken)
            .ConfigureAwait(false);

        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<ProductCatalogUpdateDto>> PullCatalogUpdatesAsync(CancellationToken cancellationToken)
    {
        ProductCatalogUpdateDto[]? updates = await httpClient
            .GetFromJsonAsync("api/catalog/updates", ErpSyncJsonContext.Default.ProductCatalogUpdateDtoArray, cancellationToken)
            .ConfigureAwait(false);

        return updates ?? [];
    }
}
