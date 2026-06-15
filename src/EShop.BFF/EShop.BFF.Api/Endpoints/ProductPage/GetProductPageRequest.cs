namespace EShop.BFF.Api.Endpoints.ProductPage;

/// <summary>Route binding for <c>GET /api/v1/bff/product-page/{productId}</c>.</summary>
public sealed class GetProductPageRequest
{
    public Guid ProductId { get; set; }
}
