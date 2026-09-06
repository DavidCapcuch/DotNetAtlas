using System.Reflection;
using Invoicing.Application.Common.Messaging;

namespace Invoicing.UnitTests.Messaging;

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
            Invoices = "invoicing.invoices",
            OrderingOrders = "ordering.orders",
            PaymentsTransactions = "payments.transactions",
            NotificationsNotifyCommands = "notifications.notify-commands",
            NotificationsNotifyEvents = "notifications.notify-events",
            DltTopicSuffix = ".Invoicing.DLT"
        };

        using var _ = new AssertionScope();

        // A topic property added to the type but never wired into GetAllTopics().
        options.GetAllTopics().Should().Contain(TopicPropertyValuesOf(options));

        // Spelled out rather than built from the options, so it disagrees when GetAllTopics() drops
        // an entry the reflection above cannot see — a dead-letter sibling — or gains a stray one.
        options.GetAllTopics().Should().BeEquivalentTo(
        [
            "invoicing.invoices",
            "notifications.notify-commands",
            "ordering.orders",
            "ordering.orders.Invoicing.DLT",
            "payments.transactions",
            "payments.transactions.Invoicing.DLT",
            "notifications.notify-events",
            "notifications.notify-events.Invoicing.DLT"
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
