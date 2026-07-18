using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Application.Categories.GetProductsByCategory;
using Catalog.Application.Common.Contracts;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Catalog.Api.Endpoints.Categories.GetProductsByCategory;

internal sealed class GetProductsByCategoryEndpoint : Endpoint<GetProductsByCategoryRequest, SearchProductsResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetProductsByCategoryQuery, SearchProductsResponse> _handler;

    public GetProductsByCategoryEndpoint(
        Platform.CQRS.IQueryHandler<GetProductsByCategoryQuery, SearchProductsResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("{id:guid}/products");
        Version(1);
        Group<CategoriesGroup>();
        Policies(AuthPolicies.ReadPolicy);
        Summary(s =>
        {
            s.Summary = "Paginated products within a category. includeDescendants=true matches by category-path prefix.";
        });
        Description(b =>
        {
            b.Produces<SearchProductsResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(GetProductsByCategoryRequest request, CancellationToken ct)
    {
        var query = new GetProductsByCategoryQuery
        {
            CategoryId = request.Id,
            IncludeDescendants = request.IncludeDescendants ?? false,
            PageNumber = request.Page ?? 1,
            PageSize = request.Limit ?? 20,
        };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}

public sealed class GetProductsByCategoryRequest
{
    public Guid Id { get; set; }

    [QueryParam]
    public bool? IncludeDescendants { get; set; }

    [QueryParam]
    public int? Page { get; set; }

    [QueryParam]
    public int? Limit { get; set; }
}
