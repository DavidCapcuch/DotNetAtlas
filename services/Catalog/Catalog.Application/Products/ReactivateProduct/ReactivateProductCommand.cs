using Platform.CQRS;

namespace Catalog.Application.Products.ReactivateProduct;

/// <summary>
/// Admin command to reactivate a discontinued product. Requires
/// <see cref="AdminReactivation"/> == <c>true</c> (policy check); 409 if the product is not
/// currently <c>Discontinued</c>.
/// </summary>
public sealed record ReactivateProductCommand : ICommand
{
    public required Guid ProductId { get; init; }

    public required bool AdminReactivation { get; init; }
}
