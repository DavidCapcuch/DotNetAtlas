using Basket.Application.Baskets.Common.Contracts;

namespace Basket.Api.Endpoints.Baskets.Checkout;

internal sealed class CheckoutBasketRequest
{
    /// <summary>
    /// Saga correlation id chosen by the caller. Must be a Guid v7 — the validator
    /// rejects v4 to protect against client-side mistakes (see
    /// <c>CheckoutBasketCommandValidator</c>).
    /// </summary>
    public required Guid CorrelationId { get; init; }

    public required CheckoutAddressDto ShippingAddress { get; init; }

    public required CheckoutAddressDto BillingAddress { get; init; }

    public required Guid PaymentMethodId { get; init; }
}
