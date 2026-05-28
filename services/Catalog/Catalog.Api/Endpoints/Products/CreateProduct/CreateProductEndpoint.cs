using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Application.Products.CreateProduct;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Catalog.Api.Endpoints.Products.CreateProduct;

internal sealed class CreateProductEndpoint : Endpoint<CreateProductRequest, CreateProductResponse>
{
    private readonly Platform.CQRS.ICommandHandler<CreateProductCommand, Guid> _handler;

    public CreateProductEndpoint(Platform.CQRS.ICommandHandler<CreateProductCommand, Guid> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post(string.Empty);
        Version(1);
        Group<ProductsGroup>();
        Policies(CatalogAuthorizationPolicies.WritePolicy);
        Idempotency();
        Summary(s =>
        {
            s.Summary = "Create a new Product in Draft status. Publishes ProductCreatedEvent on success.";
        });
        Description(b =>
        {
            b.Produces<CreateProductResponse>((int)HttpStatusCode.Created);
            b.Produces((int)HttpStatusCode.BadRequest);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.Conflict);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(CreateProductRequest request, CancellationToken ct)
    {
        var command = new CreateProductCommand
        {
            Sku = request.Sku,
            Name = request.Name,
            Description = request.Description,
            CategoryId = request.CategoryId,
            Brand = request.Brand,
            Price = request.Price,
            Dimensions = request.Dimensions,
            Images = request.Images,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            productId => Send.CreatedAtAsync<GetProductById.GetProductByIdEndpoint>(
                routeValues: new { id = productId },
                responseBody: new CreateProductResponse { ProductId = productId },
                generateAbsoluteUrl: false,
                cancellation: ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
