using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Basket.Application.Abstractions;
using Basket.Application.Baskets.Common.Errors;
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
/// <see cref="BasketAclErrors.CatalogUnavailable"/>.
/// </summary>
/// <remarks>
/// Configuration (<c>BaseAddress</c>, <c>Timeout</c>, service-auth) is applied
/// to the injected <see cref="HttpClient"/> in
/// <see cref="CatalogClientDependencyInjection.AddBasketCatalogClient"/> (W3C
/// trace context propagates automatically via OpenTelemetry); the
/// adapter itself stays transport-policy-agnostic. No Polly — cross-service
/// HTTP resilience is handled by YARP at the edge per basket.md &#xa7; 9.3.
/// </remarks>
internal sealed class ProductCatalogHttpAdapter : IProductCatalogQueryPort
{
    /// <summary>
    /// Web defaults (camelCase, case-insensitive) matching Catalog's FastEndpoints wire shape, plus
    /// strict binding: a response that drops or nulls a <em>member</em> Basket reads throws
    /// <see cref="JsonException"/> at this boundary instead of binding to <c>default</c> and
    /// surfacing as a <see cref="NullReferenceException"/> mid-mapping (basket.md &#xa7; 9.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Members only.</b> Nullability is not enforced on collection <em>elements</em>, so a
    /// <c>[null]</c> entry binds and must be guarded by hand where a record declares a collection —
    /// see the null-entry check in <see cref="FetchChunkAsync"/>. Everything below is about members.
    /// </para>
    /// <remarks>
    /// <para>
    /// The settings close different holes. <c>required</c> on the ACL records already rejects an
    /// <em>absent</em> member unaided; <see cref="JsonSerializerOptions.RespectNullableAnnotations"/>
    /// is what rejects one that is <em>present but null</em>, since presence alone satisfies the
    /// requirement. <see cref="JsonSerializerOptions.RespectRequiredConstructorParameters"/> extends
    /// the absent-member check to positional records, which <see cref="CatalogPriceDto"/> is.
    /// Duplicate properties are rejected because the JSON specification defines no behaviour for
    /// them and parsers disagree on which one wins.
    /// </para>
    /// <para>
    /// <see cref="JsonSerializerOptions.UnmappedMemberHandling"/> stays at its default, making
    /// binding <b>asymmetric by design</b>: a member Catalog drops throws, a member Catalog adds
    /// binds unaffected. Removal breaks a Basket use case; addition does not. That asymmetry is why
    /// the settings are listed individually rather than taken from
    /// <see cref="JsonSerializerOptions.Strict"/>, which bundles them with unmapped-member rejection
    /// and would turn every field Catalog adds into a failed add-item.
    /// </para>
    /// </remarks>
    /// <seealso href="https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties"/>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
    };

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
                return Result.Fail<ProductSnapshot>(BasketAclErrors.ProductNotFound(productId));
            }

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogError(
                    "Catalog returned {StatusCode} for product {ProductId}.",
                    (int)response.StatusCode,
                    productId);
                return Result.Fail<ProductSnapshot>(BasketAclErrors.CatalogUnavailable());
            }

            if (!response.IsSuccessStatusCode)
            {
                // basket.md § 9.3 bullet 4 — log at error: 4xx-other signals a
                // programming bug on our own call shape.
                _logger.LogError(
                    "Catalog returned unexpected 4xx {StatusCode} for product {ProductId}.",
                    (int)response.StatusCode,
                    productId);
                return Result.Fail<ProductSnapshot>(BasketAclErrors.CatalogUnavailable());
            }

            var dto = await response.Content
                .ReadFromJsonAsync<CatalogProductByIdResponse>(JsonOptions, ct)
                .ConfigureAwait(false);

            if (dto is null)
            {
                // A literal `null` JSON body deserializes to null, which no strict-binding setting
                // covers — those govern members, not the root. This route's members need no guard:
                // it declares no collection, so every member is reached by the options below.
                _logger.LogError(
                    "Catalog returned 200 with a null body for product {ProductId}.",
                    productId);
                return Result.Fail<ProductSnapshot>(BasketAclErrors.CatalogUnavailable());
            }

            return MapToSnapshot(sku: dto.Sku, name: dto.Name, price: dto.Price);
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
            return Result.Fail<ProductSnapshot>(BasketAclErrors.CatalogUnavailable());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Catalog network failure for product {ProductId}.", productId);
            return Result.Fail<ProductSnapshot>(BasketAclErrors.CatalogUnavailable());
        }
        catch (JsonException ex)
        {
            // Two causes, one handling: malformed JSON, or a well-formed body carrying a contract
            // Basket can no longer bind because a member it reads was dropped or nulled. The
            // exception message is the only place the two are distinguishable.
            _logger.LogError(
                ex,
                "Catalog returned a body Basket could not bind for product {ProductId}.",
                productId);
            return Result.Fail<ProductSnapshot>(BasketAclErrors.CatalogUnavailable());
        }
    }

    /// <summary>
    /// Per-request id ceiling for the Catalog by-ids batch endpoint. A 36-char GUID
    /// joined into a comma-separated query yields ~38 bytes per id; 20 ids keeps the
    /// worst-case URL under ~800 bytes — comfortably below the 2 KB cap most reverse
    /// proxies enforce. Required when callers fan out Basket.MaxItems = 50 product
    /// snapshots in one logical operation (e.g. RefreshPricesCommand).
    /// </summary>
    internal const int ByIdsChunkSize = 20;

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

        var pairs = new List<(Guid, ProductSnapshot)>(distinctIds.Length);
        for (var offset = 0; offset < distinctIds.Length; offset += ByIdsChunkSize)
        {
            var chunkLength = Math.Min(ByIdsChunkSize, distinctIds.Length - offset);
            var chunk = new ArraySegment<Guid>(distinctIds, offset, chunkLength);
            var chunkResult = await FetchChunkAsync(chunk, ct).ConfigureAwait(false);
            if (chunkResult.IsFailed)
            {
                return chunkResult.ToResult<IReadOnlyList<(Guid, ProductSnapshot)>>();
            }

            pairs.AddRange(chunkResult.Value);
        }

        return Result.Ok<IReadOnlyList<(Guid, ProductSnapshot)>>(pairs);
    }

    private async Task<Result<IReadOnlyList<(Guid ProductId, ProductSnapshot Snapshot)>>> FetchChunkAsync(
        ArraySegment<Guid> chunk,
        CancellationToken ct)
    {
        var query = string.Join(',', chunk.Select(id => id.ToString("D", CultureInfo.InvariantCulture)));
        var path = $"/api/v1/catalog/products/by-ids?ids={query}";

        try
        {
            using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogError(
                    "Catalog batch returned {StatusCode} for {Count} ids.",
                    (int)response.StatusCode,
                    chunk.Count);
                return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketAclErrors.CatalogUnavailable());
            }

            if (!response.IsSuccessStatusCode)
            {
                // basket.md § 9.3 bullet 4 — 4xx-other logs at error.
                _logger.LogError(
                    "Catalog batch returned unexpected 4xx {StatusCode} for {Count} ids.",
                    (int)response.StatusCode,
                    chunk.Count);
                return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketAclErrors.CatalogUnavailable());
            }

            var dto = await response.Content
                .ReadFromJsonAsync<CatalogProductsByIdsResponse>(JsonOptions, ct)
                .ConfigureAwait(false);

            if (dto is null)
            {
                // Same root-null guard as the single-product path — see there.
                _logger.LogError("Catalog batch returned 200 with a null body.");
                return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketAclErrors.CatalogUnavailable());
            }

            var pairs = new List<(Guid, ProductSnapshot)>(dto.Products.Count);
            foreach (var p in dto.Products)
            {
                if (p is null)
                {
                    // The one shape strict binding does not cover: System.Text.Json enforces
                    // nullability on members, not on collection elements, so `[null]` binds. Without
                    // this guard the dereference below is an uncaught NullReferenceException — a 500
                    // on the one path this ACL fails closed everywhere else.
                    _logger.LogError("Catalog batch returned a null product entry for {Count} ids.", chunk.Count);
                    return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketAclErrors.CatalogUnavailable());
                }

                var mapResult = MapToSnapshot(sku: p.Sku, name: p.Name, price: p.Price);
                if (mapResult.IsFailed)
                {
                    _logger.LogError(
                        "Failed to map Catalog product {ProductId} to snapshot — treating as upstream breakage.",
                        p.ProductId);
                    return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketAclErrors.CatalogUnavailable());
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
            _logger.LogWarning(ex, "Catalog batch request timed out for {Count} ids.", chunk.Count);
            return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketAclErrors.CatalogUnavailable());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Catalog batch network failure for {Count} ids.", chunk.Count);
            return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketAclErrors.CatalogUnavailable());
        }
        catch (JsonException ex)
        {
            // Malformed JSON or an unbindable contract — see the single-product catch.
            _logger.LogError(ex, "Catalog batch returned a body Basket could not bind for {Count} ids.", chunk.Count);
            return Result.Fail<IReadOnlyList<(Guid, ProductSnapshot)>>(BasketAclErrors.CatalogUnavailable());
        }
    }

    /// <summary>
    /// Takes the snapshot fields rather than a route's record: the wire shape is per-route and
    /// duplicated, but how a Catalog product becomes a <see cref="ProductSnapshot"/> is one rule
    /// Basket owns, and a change to it would be the same change at both call sites.
    /// </summary>
    private Result<ProductSnapshot> MapToSnapshot(string sku, string name, CatalogPriceDto price)
    {
        var moneyResult = Money.Create(price.Amount, price.Currency);
        if (moneyResult.IsFailed)
        {
            return moneyResult.ToResult<ProductSnapshot>();
        }

        return Result.Ok(ProductSnapshot.Create(
            sku: sku,
            name: name,
            price: moneyResult.Value,
            capturedAtUtc: _timeProvider.GetUtcNow()));
    }
}
