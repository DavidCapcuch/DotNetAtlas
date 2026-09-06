using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Kafka.TopicGuard;

/// <summary>
/// Registration for the Kafka required-topics readiness check.
/// </summary>
public static class KafkaTopicsExistenceHealthCheckExtensions
{
    /// <summary>
    /// Adds a readiness check that verifies, once, that every topic this service names exists on
    /// the cluster.
    /// </summary>
    /// <remarks>
    /// The broker does not auto-create topics, so producing to a name that was never provisioned
    /// does not fail fast — librdkafka queues the message and retries metadata until
    /// <c>message.timeout.ms</c> expires, then throws <c>Local: Message timed out</c>, naming
    /// neither the topic nor the reason, minutes after startup and on whichever request happened to
    /// trigger the publish. This check turns that into a named readiness failure instead.
    /// </remarks>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="bootstrapServers">Comma-separated broker list, as <c>BrokersFlat</c> builds it.</param>
    /// <param name="requiredTopics">
    /// Every topic this service produces to or consumes from, plus a dead-letter sibling for each
    /// topic it consumes — which is what each bounded context's <c>GetAllTopics()</c> returns.
    /// </param>
    /// <param name="name">Registration name, shown in the readiness payload.</param>
    /// <param name="tags">Tags selecting which probe endpoint serves this check.</param>
    public static IHealthChecksBuilder AddKafkaTopicsExistenceHealthCheck(
        this IHealthChecksBuilder builder,
        string bootstrapServers,
        IReadOnlyCollection<string> requiredTopics,
        string name,
        IReadOnlyCollection<string> tags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        ArgumentNullException.ThrowIfNull(requiredTopics);

        // An empty set is rejected here rather than tolerated downstream. It would otherwise verify
        // trivially and report "All 0 required Kafka topics" as healthy for ever — a check silently
        // checking nothing, which is the failure class this exists to delete.
        ArgumentOutOfRangeException.ThrowIfZero(requiredTopics.Count);

        // The check holds its own latch, so it is captured here rather than resolved from the
        // container: one registration, one instance, and no way to add the check without it.
        var check = new KafkaTopicsExistenceHealthCheck(
            bootstrapServers,
            requiredTopics,
            new KafkaTopicProbe(() => BuildAdminClient(bootstrapServers)));

        // No timeout: the check can only be waiting inside the blocking GetMetadata call, which
        // ignores cancellation, so a registered token would bound nothing. Declaring a number
        // nothing enforces is worse than declaring none — the probe's own timeout is the bound.
        return builder.Add(new HealthCheckRegistration(
            name,
            _ => check,
            failureStatus: HealthStatus.Unhealthy,
            tags,
            timeout: null));
    }

    private static IAdminClient BuildAdminClient(string bootstrapServers) =>
        new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers })

            // librdkafka logs every connection refusal; against a broker that is down that is a
            // wall of noise burying the one line that matters.
            .SetLogHandler((_, _) => { })
            .SetErrorHandler((_, _) => { })
            .Build();
}
