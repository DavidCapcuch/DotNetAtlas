using System.Net;
using Catalog.Api.Common.Authorization;
using Catalog.Api.Endpoints.Categories.GetCategoryTree;
using Catalog.Application.Categories.CreateCategory;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Catalog.Api.Endpoints.Categories.CreateCategory;

internal sealed class CreateCategoryEndpoint : Endpoint<CreateCategoryRequest, CreateCategoryResponse>
{
    private readonly Platform.CQRS.ICommandHandler<CreateCategoryCommand, Guid> _handler;

    public CreateCategoryEndpoint(Platform.CQRS.ICommandHandler<CreateCategoryCommand, Guid> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post(string.Empty);
        Version(1);
        Group<CategoriesGroup>();
        Policies(CatalogAuthorizationPolicies.WritePolicy);
        Idempotency();
        Summary(s =>
        {
            s.Summary = "Create a Category. Pass ParentCategoryId=null for a root category. Publishes CategoryCreatedEvent.";
        });
        Description(b =>
        {
            b.Produces<CreateCategoryResponse>((int)HttpStatusCode.Created);
            b.Produces((int)HttpStatusCode.BadRequest);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var command = new CreateCategoryCommand
        {
            Name = request.Name,
            ParentCategoryId = request.ParentCategoryId,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            categoryId => Send.CreatedAtAsync<GetCategoryTreeEndpoint>(
                routeValues: null,
                responseBody: new CreateCategoryResponse { CategoryId = categoryId },
                generateAbsoluteUrl: false,
                cancellation: ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
