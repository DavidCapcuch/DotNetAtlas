using Basket.Application.Baskets.Common.Contracts;

namespace Basket.UnitTests.Baskets.Application;

/// <summary>
/// Deterministic fixtures shared across Basket Application-layer unit tests.
/// Complements <see cref="BasketTestData"/> (domain fixtures) with DTO builders
/// that mirror what an API endpoint would deserialize from a request body.
/// </summary>
internal static class ApplicationTestData
{
    public static CheckoutAddressDto AddressDto(string countryCode = "US") => new()
    {
        Street1 = "221B Baker Street",
        Street2 = null,
        City = "London",
        State = null,
        PostalCode = "NW1 6XE",
        CountryCode = countryCode,
    };
}
