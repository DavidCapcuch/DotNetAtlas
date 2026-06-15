using System.Net.Http.Json;
using System.Text.Json;
using EShop.BFF.Infrastructure.Clients.Common;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace EShop.BFF.Infrastructure.Clients.Inventory;

/// <summary>
/// HTTP adapter for Inventory's <c>GET /api/v1/inventory/stock-items/{productId}</c> (bff.md § 4.4).
/// Any non-success (incl. 404 — stock item not initialized) collapses to a
/// <c>ServiceUnavailableError</c> "unknown availability" (bff.md § 3.1), which the page treats as a
/// partial, never as a 404. Service-auth + resilience are attached by
/// <see cref="InventoryClientDependencyInjection"/>.
/// </summary>
internal sealed class InventoryHttpClient : IInventoryClient
{
    private readonly HttpClient _http;
    private readonly ILogger<InventoryHttpClient> _logger;

    public InventoryHttpClient(HttpClient http, ILogger<InventoryHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Result<StockLevelDto>> GetStockLevelAsync(Guid productId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"api/v1/inventory/stock-items/{productId}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Inventory returned {StatusCode} for product {ProductId}; treating availability as unknown",
                    (int)response.StatusCode,
                    productId);
                return Result.Fail<StockLevelDto>(
                    InventoryClientErrors.Unavailable($"HTTP {(int)response.StatusCode}"));
            }

            var stock = await response.Content.ReadFromJsonAsync<StockLevelDto>(UpstreamJson.Web, ct);
            if (stock is null)
            {
                _logger.LogWarning("Inventory returned an empty body for product {ProductId}", productId);
                return Result.Fail<StockLevelDto>(InventoryClientErrors.Unavailable("empty response body"));
            }

            return Result.Ok(stock);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancellation — propagate unchanged
        }
        catch (Exception ex)
            when (ex is HttpRequestException
                or TaskCanceledException
                or TimeoutException
                or JsonException
                or Polly.ExecutionRejectedException)
        {
            _logger.LogWarning(ex, "Inventory call failed for product {ProductId}; treating availability as unknown", productId);
            return Result.Fail<StockLevelDto>(InventoryClientErrors.Unavailable(ex.GetType().Name));
        }
    }
}
