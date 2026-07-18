using Catalog.Application.Common.Contracts;
using Platform.CQRS;

namespace Catalog.Application.Products.GetProductById;

/// <summary>
/// Public query returning the denormalized product detail view.
/// </summary>
public sealed record GetProductByIdQuery : IQuery<ProductDetailResponse>
{
    public required Guid ProductId { get; init; }
}
