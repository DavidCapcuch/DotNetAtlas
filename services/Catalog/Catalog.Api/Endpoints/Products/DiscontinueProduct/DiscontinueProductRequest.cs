namespace Catalog.Api.Endpoints.Products.DiscontinueProduct;

public sealed class DiscontinueProductRequest
{
    public Guid Id { get; set; }

    public required string Reason { get; set; }
}
