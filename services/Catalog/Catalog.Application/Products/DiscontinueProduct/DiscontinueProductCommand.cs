using Platform.CQRS;

namespace Catalog.Application.Products.DiscontinueProduct;

/// <summary>
/// Admin command to discontinue a product. Requires a non-empty reason; succeeds only when the
/// product is <c>Active</c>.
/// </summary>
public class DiscontinueProductCommand : ICommand
{
    public required Guid ProductId { get; set; }

    public required string Reason { get; set; }
}
