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

    public async Task<Result<CatalogProductsByIdsDto>> GetProductsByIdsAsync(
        IReadOnlyList<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0)
        {
            return Result.Ok(new CatalogProductsByIdsDto([], []));
        }

        // Repeated `ids=` query params (FastEndpoints binds them into the IReadOnlyList<Guid>); Catalog's
        // validator caps the request at 100 ids (bff.md § 4.1). Catalog gates this read on catalog.read,
        // satisfied by the same client_credentials service token as GetProductByIdAsync.
        var query = "api/v1/catalog/products/by-ids?" + string.Join('&', productIds.Select(id => $"ids={id}"));

        try
        {
            using var response = await _http.GetAsync(query, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Catalog by-ids returned {StatusCode}; basket current-price enrichment dropped",
                    (int)response.StatusCode);
                return Result.Fail<CatalogProductsByIdsDto>(
                    CatalogClientErrors.Unavailable($"HTTP {(int)response.StatusCode}"));
            }

            var batch = await response.Content.ReadFromJsonAsync<CatalogProductsByIdsDto>(UpstreamJson.Web, ct);
            if (batch is null)
            {
                _logger.LogWarning("Catalog by-ids returned an empty body");
                return Result.Fail<CatalogProductsByIdsDto>(CatalogClientErrors.Unavailable("empty response body"));
            }

            return Result.Ok(batch);
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
            _logger.LogWarning(ex, "Catalog by-ids call failed; basket current-price enrichment dropped");
            return Result.Fail<CatalogProductsByIdsDto>(CatalogClientErrors.Unavailable(ex.GetType().Name));
        }
    }

    public async Task<Result<PagedResult<CatalogProductSummaryDto>>> SearchProductsAsync(
        SearchProductsRequest request, CancellationToken ct)
    {
        var query = $"api/v1/catalog/products?page={request.PageNumber}&limit={request.PageSize}";
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query += $"&status={Uri.EscapeDataString(request.Status)}";
        }

        try
        {
            using var response = await _http.GetAsync(query, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Catalog search returned {StatusCode}", (int)response.StatusCode);
                return Result.Fail<PagedResult<CatalogProductSummaryDto>>(
                    CatalogClientErrors.Unavailable($"HTTP {(int)response.StatusCode}"));
            }

            var page = await response.Content
                .ReadFromJsonAsync<PagedResult<CatalogProductSummaryDto>>(UpstreamJson.Web, ct);
            if (page is null)
            {
                _logger.LogError("Catalog search returned an empty body");
                return Result.Fail<PagedResult<CatalogProductSummaryDto>>(
                    CatalogClientErrors.Unavailable("empty response body"));
            }

            return Result.Ok(page);
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
            _logger.LogError(ex, "Catalog search call failed");
            return Result.Fail<PagedResult<CatalogProductSummaryDto>>(
                CatalogClientErrors.Unavailable(ex.GetType().Name));
        }
    }

    public async Task<Result<CategoryTreeDto>> GetCategoryTreeAsync(Guid? rootCategoryId, CancellationToken ct)
    {
        var path = "api/v1/catalog/categories/tree";
        if (rootCategoryId is { } root)
        {
            path += $"?rootCategoryId={root}";
        }

        try
        {
            using var response = await _http.GetAsync(path, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Catalog category tree returned {StatusCode}; home page will drop the tree",
                    (int)response.StatusCode);
                return Result.Fail<CategoryTreeDto>(
                    CatalogClientErrors.Unavailable($"HTTP {(int)response.StatusCode}"));
            }

            var tree = await response.Content.ReadFromJsonAsync<CategoryTreeDto>(UpstreamJson.Web, ct);
            if (tree is null)
            {
                _logger.LogWarning("Catalog category tree returned an empty body");
                return Result.Fail<CategoryTreeDto>(CatalogClientErrors.Unavailable("empty response body"));
            }

            return Result.Ok(tree);
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
            _logger.LogWarning(ex, "Catalog category tree call failed; home page will drop the tree");
            return Result.Fail<CategoryTreeDto>(CatalogClientErrors.Unavailable(ex.GetType().Name));
        }
    }
}
