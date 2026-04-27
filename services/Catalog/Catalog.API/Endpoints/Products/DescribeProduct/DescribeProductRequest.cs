namespace Catalog.API.Endpoints.Products.DescribeProduct;

public sealed class DescribeProductRequest
{
    public Guid Id { get; set; }

    public required string NewDescription { get; set; }
}
