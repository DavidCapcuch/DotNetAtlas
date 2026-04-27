using System.Net;
using Catalog.API.Common.Authorization;
using Catalog.API.Common.Extensions;
using Catalog.Application.Categories.GetCategoryTree;
using FastEndpoints;

namespace Catalog.API.Endpoints.Categories.GetCategoryTree;

internal sealed class GetCategoryTreeEndpoint : Endpoint<GetCategoryTreeRequest, GetCategoryTreeResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetCategoryTreeQuery, GetCategoryTreeResponse> _handler;

    public GetCategoryTreeEndpoint(Platform.CQRS.IQueryHandler<GetCategoryTreeQuery, GetCategoryTreeResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("tree");
        Version(1);
        Group<CategoriesGroup>();
        Policies(CatalogAuthorizationPolicies.ReadPolicy);
        Summary(s =>
        {
            s.Summary = "Fetch the category taxonomy. Pass rootCategoryId for a subtree.";
        });
        Description(b =>
        {
            b.Produces<GetCategoryTreeResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
        });
    }

    public override async Task HandleAsync(GetCategoryTreeRequest request, CancellationToken ct)
    {
        var query = new GetCategoryTreeQuery { RootCategoryId = request.RootCategoryId };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}

public sealed class GetCategoryTreeRequest
{
    [QueryParam]
    public Guid? RootCategoryId { get; set; }
}
