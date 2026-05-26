namespace Catalog.Api.Endpoints.Products.ReactivateProduct;

public sealed class ReactivateProductRequest
{
    public Guid Id { get; set; }

    /// <summary>
    /// Admin policy flag — must be <c>true</c> to reactivate. The Application layer raises
    /// <c>Product.ReactivationRequiresAdminFlag</c> (mapped to 403) when missing.
    /// </summary>
    public required bool AdminReactivation { get; set; }
}
