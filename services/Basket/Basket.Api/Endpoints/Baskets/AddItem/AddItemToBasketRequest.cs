namespace Basket.Api.Endpoints.Baskets.AddItem;

internal sealed class AddItemToBasketRequest
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }
}
