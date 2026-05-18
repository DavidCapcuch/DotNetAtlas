using Platform.CQRS;

namespace Catalog.Application.Products.DiscontinueProduct;

/// <summary>
/// Admin command to discontinue a product. Requires a non-empty reason; succeeds only when the
/// product is <c>Active</c>.
/// </summary>
public sealed record DiscontinueProductCommand : ICommand
{
    public required Guid ProductId { get; init; }

    public required string Reason { get; init; }
}
