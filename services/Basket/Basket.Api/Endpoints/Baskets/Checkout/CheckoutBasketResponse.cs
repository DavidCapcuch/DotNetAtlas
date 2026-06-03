namespace Basket.Api.Endpoints.Baskets.Checkout;

internal sealed class CheckoutBasketResponse
{
    /// <summary>
    /// The pre-assigned Order identity (UUID v7) allocated by the checkout handler
    /// (ADR-0029). The downstream Checkout Saga is keyed on this id.
    /// </summary>
    public required Guid OrderId { get; init; }
}
