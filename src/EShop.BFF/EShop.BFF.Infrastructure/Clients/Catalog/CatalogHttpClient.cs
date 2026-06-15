using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.BFF.Infrastructure.Clients.Common;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// HTTP adapter for Catalog's <c>GET /api/v1/catalog/products/{id}</c> (bff.md § 4.1). Distinguishes
/// 404 (gating <c>NotFoundError</c>) from transport failure (<c>ServiceUnavailableError</c>) so the
/// product page can 404 the former and fail-safe the latter. Service-auth + resilience are attached
/// by <see cref="CatalogClientDependencyInjection"/>.
/// </summary>
internal sealed class CatalogHttpClient : ICatalogClient
{
    private readonly HttpClient _http;
    private readonly ILogger<CatalogHttpClient> _logger;

    public CatalogHttpClient(HttpClient http, ILogger<CatalogHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Result<CatalogProductDto>> GetProductByIdAsync(Guid productId, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"api/v1/catalog/products/{productId}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Fail<CatalogProductDto>(CatalogClientErrors.ProductNotFound(productId));
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Catalog returned {StatusCode} for product {ProductId}",
                    (int)response.StatusCode,
                    productId);
                return Result.Fail<CatalogProductDto>(
                    CatalogClientErrors.Unavailable($"HTTP {(int)response.StatusCode}"));
            }

            var product = await response.Content.ReadFromJsonAsync<CatalogProductDto>(UpstreamJson.Web, ct);
            if (product is null)
            {
                _logger.LogError("Catalog returned an empty body for product {ProductId}", productId);
                return Result.Fail<CatalogProductDto>(CatalogClientErrors.Unavailable("empty response body"));
            }

            return Result.Ok(product);
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
            // Transport failure, resilience timeout, or open circuit — degrade to "unavailable".
            _logger.LogError(ex, "Catalog call failed for product {ProductId}", productId);
            return Result.Fail<CatalogProductDto>(CatalogClientErrors.Unavailable(ex.GetType().Name));
        }
    }
}
