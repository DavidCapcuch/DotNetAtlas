using System.Reflection;
using EShop.BFF.Infrastructure.Messaging.Config;

namespace EShop.BFF.UnitTests.Messaging;

/// <summary>
/// Pins the exact topic set the readiness check verifies. No dead-letter siblings: the BFF's consumer pipeline registers no AddDeadLetter middleware.
/// </summary>
public sealed class BffTopicsOptionsCoverageTests
{
    [Fact]
    public void GetAllTopics_ReturnsTheExactSetRequiredAtReadiness()
    {
        var options = new BffTopicsOptions
        {
            CatalogProducts = "catalog.products",
            CatalogCategories = "catalog.categories",
            InventoryStockEvents = "inventory.stock-events",
            BasketSessions = "basket.sessions"
        };

        using var _ = new AssertionScope();

        // A topic property added to the type but never wired into GetAllTopics().
        options.GetAllTopics().Should().Contain(TopicPropertyValuesOf(options));

        // Spelled out rather than built from the options, so it disagrees when GetAllTopics() drops
        // an entry the reflection above cannot see — a dead-letter sibling — or gains a stray one.
        options.GetAllTopics().Should().BeEquivalentTo(
        [
            "catalog.products",
            "catalog.categories",
            "inventory.stock-events",
            "basket.sessions"
        ]);
    }

    /// <summary>Every string property except the suffix, which is a fragment rather than a topic.</summary>
    private static IEnumerable<string> TopicPropertyValuesOf(BffTopicsOptions options) =>
        typeof(BffTopicsOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => (string)property.GetValue(options)!);
}
