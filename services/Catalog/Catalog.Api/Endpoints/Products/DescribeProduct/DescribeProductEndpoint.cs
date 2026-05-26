using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Api.Common.Extensions;
using Catalog.Application.Products.DescribeProduct;
using FastEndpoints;

namespace Catalog.Api.Endpoints.Products.DescribeProduct;

internal sealed class DescribeProductEndpoint : Endpoint<DescribeProductRequest>
{
    private readonly Platform.CQRS.ICommandHandler<DescribeProductCommand> _handler;

    public DescribeProductEndpoint(Platform.CQRS.ICommandHandler<DescribeProductCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Put("{id:guid}/description");
        Version(1);
        Group<ProductsGroup>();
        Policies(CatalogAuthorizationPolicies.WritePolicy);
        Summary(s =>
        {
            s.Summary = "Overwrite a product's description. 409 on discontinued; 422 on invalid description.";
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Conflict);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(DescribeProductRequest request, CancellationToken ct)
    {
        var command = new DescribeProductCommand
        {
            ProductId = request.Id,
            NewDescription = request.NewDescription,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
