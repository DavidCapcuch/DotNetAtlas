using Basket.Api.Endpoints.Baskets.Checkout;
using Basket.Application.Baskets.Common.Contracts;
using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.IntegrationTests.Common;

/// <summary>Canonical test data shared across Basket integration tests.</summary>
internal static class BasketTestData
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

    /// <summary>
    /// Pass explicitly every snapshot field the test asserts on, so the asserted value is visible
    /// where it is asserted. Sku and name have no parameter yet — add one before asserting them.
    /// </summary>
    public static ProductSnapshot Snapshot(decimal price = 10m, string currency = "EUR") =>
        ProductSnapshot.Create(
            sku: "SKU",
            name: "Product",
            price: Money.Create(price, currency).Value,
            capturedAtUtc: CapturedAt);

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
