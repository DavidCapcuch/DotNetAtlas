using System.Net;
using Catalog.API.Common.Authorization;
using Catalog.API.Common.Extensions;
using Catalog.Application.Products.GetProductById;
using FastEndpoints;

namespace Catalog.API.Endpoints.Products.GetProductById;

internal sealed class GetProductByIdEndpoint : Endpoint<GetProductByIdRequest, GetProductByIdResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetProductByIdQuery, GetProductByIdResponse> _handler;

    public GetProductByIdEndpoint(Platform.CQRS.IQueryHandler<GetProductByIdQuery, GetProductByIdResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get("{id:guid}");
        Version(1);
        Group<ProductsGroup>();
        Policies(CatalogAuthorizationPolicies.ReadPolicy);
        Summary(s =>
        {
            s.Summary = "Fetch a single product's full detail view (read from product_search_view projection).";
        });
        Description(b =>
        {
            b.Produces<GetProductByIdResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
        });
    }

    public override async Task HandleAsync(GetProductByIdRequest request, CancellationToken ct)
    {
        var query = new GetProductByIdQuery { ProductId = request.Id };

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}

public sealed class GetProductByIdRequest
{
    public Guid Id { get; set; }
}
