using Platform.CQRS;

namespace Catalog.Application.Products.ReactivateProduct;

/// <summary>
/// Admin command to reactivate a discontinued product. Requires
/// <see cref="AdminReactivation"/> == <c>true</c> (policy check); 409 if the product is not
/// currently <c>Discontinued</c>.
/// </summary>
public class ReactivateProductCommand : ICommand
{
    public required Guid ProductId { get; set; }

    public required bool AdminReactivation { get; set; }
}
