using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets;

/// <summary>
/// Deterministic constructors for domain test fixtures — shared across the Basket
/// unit-test suite to keep setups concise and to avoid drifting currencies / SKUs
/// across tests.
/// </summary>
internal static class BasketTestData
{
    public static readonly DateTimeOffset DefaultCapturedAt =
        new(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

    public static ProductSnapshot Snapshot(
        decimal amount = 10m,
        CurrencyCode? currency = null,
        string sku = "SKU-1",
        string name = "Product 1",
        DateTimeOffset? capturedAtUtc = null)
    {
        return ProductSnapshot.Create(
            sku,
            name,
            new Money(amount, currency ?? CurrencyCode.Usd),
            capturedAtUtc ?? DefaultCapturedAt);
    }

    /// <summary>
    /// A deterministic <see cref="Address"/> fixture for checkout tests. The
    /// courier-field pass-through is tested by equality; the specific values
    /// are not meaningful beyond ISO 3166-1 alpha-2 correctness.
    /// </summary>
    public static Address Address(string countryCode = "US") => Platform.SharedKernel.ValueObjects.Address
        .Create("221B Baker Street", null, "London", null, "NW1 6XE", countryCode)
        .Value;
}
