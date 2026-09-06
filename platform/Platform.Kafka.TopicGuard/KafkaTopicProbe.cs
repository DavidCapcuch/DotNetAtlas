using Confluent.Kafka;

namespace Platform.Kafka.TopicGuard;

/// <summary>
/// Asks the cluster which of a required topic set does not exist.
/// </summary>
/// <remarks>
/// Never creates a topic, and the way it avoids doing so is load-bearing rather than incidental.
/// <c>IAdminClient.GetMetadata(string, TimeSpan)</c> asks the broker about one named topic, and a
/// broker with <c>auto.create.topics.enable=true</c> creates it in the course of answering — so a
/// check written the obvious way would provision the very topic whose absence it reports, and would
/// then pass for ever. The all-topics overload names nothing and therefore cannot create anything.
/// Do not "optimise" it back.
/// </remarks>
internal sealed class KafkaTopicProbe(Func<IAdminClient> adminClientFactory)
{
    /// <summary>
    /// Returns the required topics the cluster did not report, empty when all exist.
    /// </summary>
    /// <exception cref="KafkaException">The broker did not answer. The caller reports that as its
    /// own failure rather than as a missing topic — not knowing is not the same as knowing it is
    /// absent.</exception>
    public IReadOnlyCollection<string> FindMissingTopics(
        IReadOnlyCollection<string> requiredTopics,
        TimeSpan timeout)
    {
        using var adminClient = adminClientFactory();
        var metadata = adminClient.GetMetadata(timeout);

        // A broker list this short means the client never talked to anyone. Reading it as "the
        // cluster has no topics" would report every required topic missing on what is really a
        // connectivity failure.
        if (metadata.Brokers.Count == 0)
        {
            throw new KafkaException(ErrorCode.Local_AllBrokersDown);
        }

        var existing = metadata.Topics
            .Where(topic => !topic.Error.IsError)
            .Select(topic => topic.Topic)
            .ToHashSet(StringComparer.Ordinal);

        return [.. requiredTopics.Where(topic => !existing.Contains(topic))];
    }
}
