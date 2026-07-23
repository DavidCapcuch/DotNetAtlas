using Platform.CQRS;

namespace Catalog.Application.Products.UpdateProductPrice;

/// <summary>
/// Admin command to reprice a product. Carries only the new amount — a product's price currency is
/// fixed for its lifetime (ADR-0002). No-op when the new amount matches the current one; 409 when
/// the product is <c>Discontinued</c>.
/// </summary>
public sealed record UpdateProductPriceCommand : ICommand
{
    public required Guid ProductId { get; init; }

    public required decimal NewAmount { get; init; }
}
