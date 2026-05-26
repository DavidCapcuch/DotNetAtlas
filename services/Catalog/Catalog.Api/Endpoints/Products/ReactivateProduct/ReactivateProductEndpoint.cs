using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Api.Common.Extensions;
using Catalog.Application.Products.ReactivateProduct;
using FastEndpoints;

namespace Catalog.Api.Endpoints.Products.ReactivateProduct;

internal sealed class ReactivateProductEndpoint : Endpoint<ReactivateProductRequest>
{
    private readonly Platform.CQRS.ICommandHandler<ReactivateProductCommand> _handler;

    public ReactivateProductEndpoint(Platform.CQRS.ICommandHandler<ReactivateProductCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("{id:guid}/reactivate");
        Version(1);
        Group<ProductsGroup>();
        Policies(CatalogAuthorizationPolicies.WritePolicy);
        // No .Idempotency() — ADR-0013 makes this optional and reactivation is a low-volume
        // admin operation. Re-issuing returns the same 409 ("not discontinued") on the second
        // call, which is harmless.
        Summary(s =>
        {
            s.Summary = "Reactivate a discontinued product. Requires AdminReactivation=true (403 otherwise).";
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Conflict);
        });
    }

    public override async Task HandleAsync(ReactivateProductRequest request, CancellationToken ct)
    {
        var command = new ReactivateProductCommand
        {
            ProductId = request.Id,
            AdminReactivation = request.AdminReactivation,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
