using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Kafka.TopicGuard;

/// <summary>
/// Readiness check reporting whether the topics this service names actually exist.
/// </summary>
/// <remarks>
/// <para>
/// Latched: once the cluster has confirmed the whole set, the check never contacts the broker
/// again. Topic existence is not something that changes under a running service, so re-asking on
/// every probe would put a metadata call per service per probe interval on the cluster to learn
/// nothing. The cost of the latch is that a topic deleted at runtime goes unnoticed — accepted
/// deliberately.
/// </para>
/// <para>
/// A broker that does not answer propagates out as an exception, which the health-check pipeline
/// reports as unhealthy with the cause attached. That is the honest outcome: an instance that
/// could not ask knows no more about its topics than one that found them missing.
/// </para>
/// </remarks>
internal sealed class KafkaTopicsExistenceHealthCheck(
    string bootstrapServers,
    IReadOnlyCollection<string> requiredTopics,
    KafkaTopicProbe probe) : IHealthCheck
{
    /// <summary>
    /// Bounds the one blocking metadata call. Sits under Compose's 5s readiness-probe budget and
    /// under the 4s ceiling every other Kafka check is capped at — at or above it the orchestrator
    /// aborts the request first, and the container's verdict reflects a probe abort rather than
    /// this check's answer. <c>GetMetadata</c> ignores cancellation, so this argument is the only
    /// thing that bounds it and the registration declares no timeout it could not enforce.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private volatile bool _verified;
    private int _probeInFlight;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_verified)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"All {requiredTopics.Count} required Kafka topics were verified on this instance."));
        }

        // A probe already running is the answer being computed. Queueing behind it would stack
        // readiness probes into a chain of blocking metadata calls, each holding a request thread.
        if (Interlocked.CompareExchange(ref _probeInFlight, 1, 0) != 0)
        {
            return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus,
                "Kafka topic verification is already in progress."));
        }

        try
        {
            var missing = probe.FindMissingTopics(requiredTopics, ProbeTimeout);
            if (missing.Count > 0)
            {
                return Task.FromResult(new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Kafka topics unavailable on {bootstrapServers}: "
                    + $"{string.Join(", ", missing)}. Either never provisioned "
                    + "(see docs/kafka-topology.md), or reported by the cluster with an error "
                    + "such as a leader election still settling."));
            }

            _verified = true;
            return Task.FromResult(HealthCheckResult.Healthy(
                $"All {requiredTopics.Count} required Kafka topics were verified on this instance."));
        }
        finally
        {
            Volatile.Write(ref _probeInFlight, 0);
        }
    }
}
