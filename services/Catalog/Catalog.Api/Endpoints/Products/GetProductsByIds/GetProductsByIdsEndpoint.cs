using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Api.Common.Extensions;
using Catalog.Application.Products.GetProductsByIds;
using FastEndpoints;

namespace Catalog.Api.Endpoints.Products.GetProductsByIds;

/// <summary>
/// Bulk product lookup for BFF / basket / order enrichment. Partial-tolerant: any IDs not
/// found are returned in <see cref="GetProductsByIdsResponse.MissingProductIds"/>.
/// Validator (Application layer) caps the request at 100 IDs.
/// </summary>
internal sealed class GetProductsByIdsEndpoint : Endpoint<GetProductsByIdsRequest, GetProductsByIdsResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetProductsByIdsQuery, GetProductsByIdsResponse> _handler;

    public GetProductsByIdsEndpoint(Platform.CQRS.IQueryHandler<GetProductsByIdsQuery, GetProductsByIdsResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("by-ids");
        Version(1);
        Group<ProductsGroup>();
        Policies(CatalogAuthorizationPolicies.ReadPolicy);
        Summary(s =>
        {
            s.Summary = "Bulk product lookup. Partial-tolerant; max 100 IDs per call.";
        });
        Description(b =>
        {
            b.Produces<GetProductsByIdsResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(GetProductsByIdsRequest request, CancellationToken ct)
    {
        var query = new GetProductsByIdsQuery { Ids = request.Ids ?? [] };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}

public sealed class GetProductsByIdsRequest
{
    [QueryParam]
    [BindFrom("ids")]
    public IReadOnlyList<Guid>? Ids { get; set; }
}
