using Basket.Api.Endpoints.Baskets.Checkout;
using Basket.Application.Baskets.Common.Contracts;

namespace Basket.IntegrationTests.Common;

/// <summary>Canonical request payloads shared across Basket integration tests.</summary>
internal static class BasketTestData
{
    public static CheckoutBasketRequest ValidCheckoutRequest()
    {
        var address = new CheckoutAddressDto
        {
            Street1 = "Wenceslas Square 1",
            City = "Prague",
            PostalCode = "11000",
            CountryCode = "CZ",
        };

        return new CheckoutBasketRequest
        {
            ShippingAddress = address,
            BillingAddress = address,
            PaymentMethodId = Guid.CreateVersion7(),
        };
    }
}
