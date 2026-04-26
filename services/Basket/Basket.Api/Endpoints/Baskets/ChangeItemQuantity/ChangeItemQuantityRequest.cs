using FastEndpoints;

namespace Basket.Api.Endpoints.Baskets.ChangeItemQuantity;

internal sealed class ChangeItemQuantityRequest
{
    [BindFrom("productId")]
    public required Guid ProductId { get; init; }

    public required int NewQuantity { get; init; }
}
