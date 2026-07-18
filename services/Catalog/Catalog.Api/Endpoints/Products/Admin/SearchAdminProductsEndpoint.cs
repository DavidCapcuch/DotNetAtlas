using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Api.Endpoints.Products.SearchProducts;
using Catalog.Application.Common.Contracts;
using Catalog.Application.Products.SearchProducts;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Catalog.Api.Endpoints.Products.Admin;

/// <summary>
/// Admin-side counterpart to <see cref="SearchProductsEndpoint"/>: searches the
/// <c>product_search_view</c> projection with no Active-only filter and no dependency on the
/// <c>catalog.show-discontinued-in-search</c> feature flag. Gated by <c>catalog.write</c>
/// scope so non-admin readers cannot reach it (#172 — admin search endpoint).
/// </summary>
internal sealed class SearchAdminProductsEndpoint
    : Endpoint<SearchProductsRequest, SearchProductsResponse>
{
    private readonly Platform.CQRS.IQueryHandler<SearchProductsQuery, SearchProductsResponse> _handler;

    public SearchAdminProductsEndpoint(
        Platform.CQRS.IQueryHandler<SearchProductsQuery, SearchProductsResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get(string.Empty);
        Version(1);
        Group<AdminProductsGroup>();
        Policies(AuthPolicies.WritePolicy);
        Summary(s =>
        {
            s.Summary =
                "Admin-scoped catalog search. Exposes all statuses (including Discontinued) without depending on the ShowDiscontinuedInSearch feature flag.";
        });
        Description(b =>
        {
            b.Produces<SearchProductsResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
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
            IncludeAllStatuses = true,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
