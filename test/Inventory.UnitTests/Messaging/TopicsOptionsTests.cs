using Inventory.Application.Common.Messaging;

namespace Inventory.UnitTests.Messaging;

/// <summary>
/// One bounded context is covered rather than all eight, and Inventory is the widest: three
/// consumed topics and two produced-only ones, so both halves of the produced-topics-get-no-DLT
/// rule are exercised in a single set. Catalog and Invoicing could carry the same test — they also
/// have produced-only topics — so this is a sampling choice, not a claim that Inventory is the only
/// place the rule can break. The expected set is read off the <c>kafka-create-topic</c> block in
/// <c>docker-compose.yaml</c> — the runtime source of truth — rather than recomputed the way the
/// code computes it.
/// </summary>
public sealed class TopicsOptionsTests
{
    [Fact]
    public void GetAllTopics_ReturnsTheProvisionedTopicsInventoryUses_WithDeadLettersOnlyForConsumedOnes()
    {
        var topics = new TopicsOptions
        {
            InventoryStockEvents = "inventory.stock-events",
            InventoryReservations = "inventory.reservations",
            InventoryReservationCommands = "inventory.reservation-commands",
            CatalogProducts = "catalog.products",
            OrderingOrders = "ordering.orders",
            DltTopicSuffix = ".Inventory.DLT"
        };

        topics.GetAllTopics().Should().BeEquivalentTo(
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
}
