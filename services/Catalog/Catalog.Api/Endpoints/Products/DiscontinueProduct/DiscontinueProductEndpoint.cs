using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Api.Common.Extensions;
using Catalog.Application.Products.DiscontinueProduct;
using FastEndpoints;

namespace Catalog.Api.Endpoints.Products.DiscontinueProduct;

internal sealed class DiscontinueProductEndpoint : Endpoint<DiscontinueProductRequest>
{
    private readonly Platform.CQRS.ICommandHandler<DiscontinueProductCommand> _handler;

    public DiscontinueProductEndpoint(Platform.CQRS.ICommandHandler<DiscontinueProductCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("{id:guid}/discontinue");
        Version(1);
        Group<ProductsGroup>();
        Policies(CatalogAuthorizationPolicies.WritePolicy);
        Idempotency();
        Summary(s =>
        {
            s.Summary = "Discontinue an Active product. Requires non-empty reason. Publishes ProductDiscontinuedEvent.";
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

    public override async Task HandleAsync(DiscontinueProductRequest request, CancellationToken ct)
    {
        var command = new DiscontinueProductCommand
        {
            ProductId = request.Id,
            Reason = request.Reason,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
