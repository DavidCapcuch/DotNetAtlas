using System.Reflection;
using Payments.Application.Common.Messaging;

namespace Payments.UnitTests.Messaging;

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
            Transactions = "payments.transactions",
            PaymentCommands = "payments.payment-commands",
            DltTopicSuffix = ".Payments.DLT"
        };

        using var _ = new AssertionScope();

        // A topic property added to the type but never wired into GetAllTopics().
        options.GetAllTopics().Should().Contain(TopicPropertyValuesOf(options));

        // Spelled out rather than built from the options, so it disagrees when GetAllTopics() drops
        // an entry the reflection above cannot see — a dead-letter sibling — or gains a stray one.
        options.GetAllTopics().Should().BeEquivalentTo(
        [
            "payments.transactions",
            "payments.payment-commands",
            "payments.payment-commands.Payments.DLT"
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
