using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Api.Common.Extensions;
using Catalog.Application.Categories.ReparentCategory;
using FastEndpoints;

namespace Catalog.Api.Endpoints.Categories.ReparentCategory;

internal sealed class ReparentCategoryEndpoint : Endpoint<ReparentCategoryRequest>
{
    private readonly Platform.CQRS.ICommandHandler<ReparentCategoryCommand> _handler;

    public ReparentCategoryEndpoint(Platform.CQRS.ICommandHandler<ReparentCategoryCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Put("{id:guid}/reparent");
        Version(1);
        Group<CategoriesGroup>();
        Policies(CatalogAuthorizationPolicies.WritePolicy);
        Summary(s =>
        {
            s.Summary = "Reparent a category. Cycle-detected (422) and self-parent (422) guarded by Application layer.";
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(ReparentCategoryRequest request, CancellationToken ct)
    {
        var command = new ReparentCategoryCommand
        {
            CategoryId = request.Id,
            NewParentCategoryId = request.NewParentCategoryId,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
