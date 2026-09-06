using System.Reflection;
using Inventory.Application.Common.Messaging;

namespace Inventory.UnitTests.Messaging;

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
            InventoryStockEvents = "inventory.stock-events",
            InventoryReservations = "inventory.reservations",
            InventoryReservationCommands = "inventory.reservation-commands",
            CatalogProducts = "catalog.products",
            OrderingOrders = "ordering.orders",
            DltTopicSuffix = ".Inventory.DLT"
        };

        using var _ = new AssertionScope();

        // A topic property added to the type but never wired into GetAllTopics().
        options.GetAllTopics().Should().Contain(TopicPropertyValuesOf(options));

        // Spelled out rather than built from the options, so it disagrees when GetAllTopics() drops
        // an entry the reflection above cannot see — a dead-letter sibling — or gains a stray one.
        options.GetAllTopics().Should().BeEquivalentTo(
        [
            "inventory.stock-events",
            "inventory.reservations",
            "inventory.reservation-commands",
            "inventory.reservation-commands.Inventory.DLT",
            "catalog.products",
            "catalog.products.Inventory.DLT",
            "ordering.orders",
            "ordering.orders.Inventory.DLT"
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
