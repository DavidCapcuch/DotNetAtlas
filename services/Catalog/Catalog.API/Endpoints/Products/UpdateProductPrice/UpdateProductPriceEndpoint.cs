using System.Net;
using Catalog.API.Common.Authorization;
using Catalog.API.Common.Extensions;
using Catalog.Application.Products.UpdateProductPrice;
using FastEndpoints;

namespace Catalog.API.Endpoints.Products.UpdateProductPrice;

internal sealed class UpdateProductPriceEndpoint : Endpoint<UpdateProductPriceRequest>
{
    private readonly Platform.CQRS.ICommandHandler<UpdateProductPriceCommand> _handler;

    public UpdateProductPriceEndpoint(Platform.CQRS.ICommandHandler<UpdateProductPriceCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Put("{id:guid}/price");
        Version(1);
        Group<ProductsGroup>();
        Policies(CatalogAuthorizationPolicies.WritePolicy);
        Summary(s =>
        {
            s.Summary = "Update a product's price. Publishes ProductPriceChanged on change; no-op on identical price.";
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

    public override async Task HandleAsync(UpdateProductPriceRequest request, CancellationToken ct)
    {
        var command = new UpdateProductPriceCommand
        {
            ProductId = request.Id,
            NewPrice = request.NewPrice,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
