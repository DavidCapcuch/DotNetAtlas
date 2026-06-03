using Basket.Application.Baskets.Common.Contracts;

namespace Basket.Api.Endpoints.Baskets.Checkout;

internal sealed class CheckoutBasketRequest
{
    public required CheckoutAddressDto ShippingAddress { get; init; }

    public required CheckoutAddressDto BillingAddress { get; init; }

    public required Guid PaymentMethodId { get; init; }
}
