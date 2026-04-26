using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Basket.Application.Abstractions;
using Basket.Domain.Baskets.Errors;
using Basket.Domain.Baskets.ValueObjects;
using FluentResults;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// HTTP implementation of the Catalog Anti-Corruption Layer port
/// (<see cref="IProductCatalogQueryPort"/>). Translates Catalog's transport
/// DTOs into Basket's internal <see cref="ProductSnapshot"/> VO, classifies
/// HTTP outcomes into the <see cref="BasketErrors"/> error taxonomy, and
/// propagates caller cancellation unchanged while mapping
/// <see cref="HttpClient"/>-internal timeouts to
/// <see cref="BasketErrors.CatalogUnavailable"/>.
/// </summary>
/// <remarks>
/// Configuration (<c>BaseAddress</c>, <c>Timeout</c>, service-auth, correlation-id
/// propagation) is applied to the injected <see cref="HttpClient"/> in
/// <see cref="CatalogClientDependencyInjection.AddBasketCatalogClient"/>; the
/// adapter itself stays transport-policy-agnostic. No Polly — cross-service
/// HTTP resilience is handled by YARP at the edge per basket.md &#xa7; 9.3.
/// </remarks>
internal sealed class ProductCatalogHttpAdapter : IProductCatalogQueryPort
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProductCatalogHttpAdapter> _logger;

    public ProductCatalogHttpAdapter(
        HttpClient http,
        TimeProvider timeProvider,
        ILogger<ProductCatalogHttpAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ProductSnapshot>> GetProductSnapshotAsync(Guid productId, CancellationToken ct)
    {
        var path = $"/api/v1/catalog/products/{productId.ToString("D", CultureInfo.InvariantCulture)}";

        try
        {
            using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Fail<ProductSnapshot>(BasketErrors.ProductNotFound(productId));
            }

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogError(
                    "Catalog returned {StatusCode} for product {ProductId}.",
                    (int)response.StatusCode,
                    productId);
                return Result.Fail<ProductSnapshot>(BasketErrors.CatalogUnavailable());
            }

            if (!response.IsSuccessStatusCode)
            {
                // basket.md § 9.3 bullet 4 — log at error: 4xx-other signals a
                // programming bug on our own call shape.
                _logger.LogError(
                    "Catalog returned unexpected 4xx {StatusCode} for product {ProductId}.",
                    (int)response.StatusCode,
                    productId);
                return Result.Fail<ProductSnapshot>(BasketErrors.CatalogUnavailable());
            }

            var dto = await response.Content
                .ReadFromJsonAsync<CatalogProductResponse>(JsonOptions, ct)
                .ConfigureAwait(false);

            if (dto is null || dto.Price is null)
            {
                // Null body or null nested Price — System.Text.Json does not
                // enforce non-nullable-reference annotations, so defend against
                // protocol drift and treat as upstream breakage.
                _logger.LogError(
                    "Catalog returned 200 with null or incomplete body for product {ProductId}.",
                    productId);
                return Result.Fail<ProductSnapshot>(BasketErrors.CatalogUnavailable());
            }

            return MapToSnapshot(dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-initiated cancellation — propagate per idiomatic .NET. Must
            // come before the TaskCanceledException catch because
            // TaskCanceledException derives from OperationCanceledException.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient.Timeout firing (.NET 5+ surfaces this as
            // TaskCanceledException with inner TimeoutException). Caller did NOT
            // cancel — map to CatalogUnavailable per basket.md § 9.3.
            _logger.LogWarning(ex, "Catalog request timed out for product {ProductId}.", productId);
            return Result.Fail<ProductSnapshot>(BasketErrors.CatalogUnavailable());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Catalog network failure for product {ProductId}.", productId);
            return Result.Fail<ProductSnapshot>(BasketErrors.CatalogUnavailable());
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Catalog returned malformed JSON for product {ProductId}.", productId);
            return Result.Fail<ProductSnapshot>(BasketErrors.CatalogUnavailable());
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<(Guid ProductId, ProductSnapshot Snapshot)>>> GetManyAsync(
        IEnumerable<Guid> productIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        var distinctIds = productIds.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return Result.Ok<IReadOnlyList<(Guid, ProductSnapshot)>>(Array.Empty<(Guid, ProductSnapshot)>());
        }

        var query = string.Join(
            ',',
            distinctIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture)));
        var path = $"/api/v1/catalog/products/by-ids?ids={query}";

        try
        {
            using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogError(
                    "Catalog batch returned {StatusCode} for {Count} ids.",
                    (int)response.StatusCode,
                    distinctIds.Length);
                return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketErrors.CatalogUnavailable());
            }

            if (!response.IsSuccessStatusCode)
            {
                // basket.md § 9.3 bullet 4 — 4xx-other logs at error.
                _logger.LogError(
                    "Catalog batch returned unexpected 4xx {StatusCode} for {Count} ids.",
                    (int)response.StatusCode,
                    distinctIds.Length);
                return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketErrors.CatalogUnavailable());
            }

            var dto = await response.Content
                .ReadFromJsonAsync<CatalogProductsByIdsResponse>(JsonOptions, ct)
                .ConfigureAwait(false);

            if (dto is null || dto.Products is null)
            {
                // Null body or null Products array — same protocol-drift
                // defence as the single-product path.
                _logger.LogError("Catalog batch returned 200 with null or incomplete body.");
                return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketErrors.CatalogUnavailable());
            }

            var pairs = new List<(Guid, ProductSnapshot)>(dto.Products.Count);
            foreach (var p in dto.Products)
            {
                var mapResult = MapToSnapshot(p);
                if (mapResult.IsFailed)
                {
                    _logger.LogError(
                        "Failed to map Catalog product {ProductId} to snapshot — treating as upstream breakage.",
                        p.ProductId);
                    return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketErrors.CatalogUnavailable());
                }

                pairs.Add((p.ProductId, mapResult.Value));
            }

            return Result.Ok<IReadOnlyList<(Guid, ProductSnapshot)>>(pairs);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Catalog batch request timed out for {Count} ids.", distinctIds.Length);
            return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketErrors.CatalogUnavailable());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Catalog batch network failure for {Count} ids.", distinctIds.Length);
            return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketErrors.CatalogUnavailable());
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Catalog batch returned malformed JSON.");
            return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketErrors.CatalogUnavailable());
        }
    }

    private Result<ProductSnapshot> MapToSnapshot(CatalogProductResponse dto)
    {
        var moneyResult = Money.Create(dto.Price.Amount, dto.Price.Currency);
        if (moneyResult.IsFailed)
        {
            return moneyResult.ToResult<ProductSnapshot>();
        }

        return Result.Ok(ProductSnapshot.Create(
            sku: dto.Sku,
            name: dto.Name,
            price: moneyResult.Value,
            capturedAtUtc: _timeProvider.GetUtcNow()));
    }
}
