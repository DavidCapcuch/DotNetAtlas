using Platform.CQRS;

namespace Catalog.Application.Products.GetProductById;

/// <summary>
/// Public query returning the denormalized product detail view.
/// </summary>
public sealed record GetProductByIdQuery : IQuery<GetProductByIdResponse>
{
    public required Guid ProductId { get; init; }
}
