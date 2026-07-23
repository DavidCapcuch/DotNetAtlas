namespace Catalog.Api.Endpoints.Products.UpdateProductPrice;

/// <summary>
/// Reprice request body. Carries only the new amount; the product's price currency is fixed at
/// creation (ADR-0002) and is not an input to a reprice.
/// </summary>
public sealed class UpdateProductPriceRequest
{
    public Guid Id { get; set; }

    public required decimal NewAmount { get; set; }
}
