using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Application.Common.Contracts;
using Catalog.Application.Products.SearchProducts;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Catalog.Api.Endpoints.Products.SearchProducts;

/// <summary>
/// Paginated full-text + faceted search over the <c>product_search_view</c> projection.
/// All filters are optional; discontinued items are hidden unless the
/// <c>catalog.show-discontinued-in-search</c> feature flag (ADR-0014) is enabled.
/// </summary>
internal sealed class SearchProductsEndpoint : Endpoint<SearchProductsRequest, SearchProductsResponse>
{
    private readonly Platform.CQRS.IQueryHandler<SearchProductsQuery, SearchProductsResponse> _handler;

    public SearchProductsEndpoint(Platform.CQRS.IQueryHandler<SearchProductsQuery, SearchProductsResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get(string.Empty);
        Version(1);
        Group<ProductsGroup>();
        Policies(AuthPolicies.ReadPolicy);
        Summary(s =>
        {
            s.Summary =
                "Paginated catalog search with optional text, category-prefix, price-range, and status filters.";
        });
        Description(b =>
        {
            b.Produces<SearchProductsResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(SearchProductsRequest request, CancellationToken ct)
    {
        var query = new SearchProductsQuery
        {
            Text = request.Text,
            CategoryPathPrefix = request.CategoryPath,
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            Currency = request.Currency,
            Status = request.Status,
            PageNumber = request.Page ?? 1,
            PageSize = request.Limit ?? 20,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}

public sealed class SearchProductsRequest
{
    [QueryParam]
    public string? Text { get; set; }

    [QueryParam]
    public string? CategoryPath { get; set; }

    [QueryParam]
    public decimal? MinPrice { get; set; }

    [QueryParam]
    public decimal? MaxPrice { get; set; }

    [QueryParam]
    public string? Currency { get; set; }

    [QueryParam]
    public string? Status { get; set; }

    [QueryParam]
    public int? Page { get; set; }

    [QueryParam]
    public int? Limit { get; set; }
}
