using Platform.CQRS;

namespace Catalog.Application.Products.DescribeProduct;

/// <summary>
/// Admin command to overwrite a product's description. 409 on discontinued product; 422 on
/// invalid description content (HTML rejected, max length enforced).
/// </summary>
public sealed record DescribeProductCommand : ICommand
{
    public required Guid ProductId { get; init; }

    public required string NewDescription { get; init; }
}
