using System.Reflection;
using Catalog.Application.Common.Messaging;

namespace Catalog.UnitTests.Messaging;

/// <summary>
/// Pins the exact topic set the readiness check verifies.
/// </summary>
public sealed class TopicsOptionsCoverageTests
{
    [Fact]
    public void GetAllTopics_ReturnsTheExactSetRequiredAtReadiness()
    {
        var options = new TopicsOptions
        {
            CatalogProducts = "catalog.products",
            CatalogCategories = "catalog.categories",
            InventoryStockEvents = "inventory.stock-events",
            DltTopicSuffix = ".Catalog.DLT"
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
            "inventory.stock-events.Catalog.DLT"
        ]);
    }

    /// <summary>Every string property except the suffix, which is a fragment rather than a topic.</summary>
    private static IEnumerable<string> TopicPropertyValuesOf(TopicsOptions options) =>
        typeof(TopicsOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string)
                               && property.Name != nameof(TopicsOptions.DltTopicSuffix))
            .Select(property => (string)property.GetValue(options)!);
}
