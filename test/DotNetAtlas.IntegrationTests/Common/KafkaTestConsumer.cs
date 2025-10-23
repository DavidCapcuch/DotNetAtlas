using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace DotNetAtlas.IntegrationTests.Common;

/// <summary>
/// Non-generic interface for Kafka test consumers.
/// Used for storing different consumer types in a typed collection.
/// </summary>
public interface IKafkaTestConsumer : IDisposable
{
}

/// <summary>
/// Generic Kafka test consumer for integration tests.
/// Creates a unique consumer group per instance to avoid conflicts.
/// </summary>
/// <typeparam name="TValue">The Avro message type to deserialize.</typeparam>
public sealed class KafkaTestConsumer<TValue> : IKafkaTestConsumer
    where TValue : ISpecificRecord
{
    private readonly CachedSchemaRegistryClient _schemaClient;
    private readonly IConsumer<string, TValue> _consumer;

    public KafkaTestConsumer(string bootstrapServers, string schemaRegistryUrl, string topic)
    {
        _schemaClient = new CachedSchemaRegistryClient(new SchemaRegistryConfig
        {
            Url = schemaRegistryUrl
        });
        _consumer = new ConsumerBuilder<string, TValue>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"test-consumer-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        })
            .SetValueDeserializer(new AvroDeserializer<TValue>(_schemaClient).AsSyncOverAsync())
            .Build();

        _consumer.Subscribe(topic);
    }

    /// <summary>
    /// Consumes one message from the specified topic within the timeout period.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for a message.</param>
    /// <param name="cancellationToken">Optional cancellation token to cancel the operation.</param>
    /// <returns>The deserialized message value, or null if no message was received within the timeout.</returns>
    public TValue? ConsumeOne(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(timeout);
                if (consumeResult?.Message != null)
                {
                    _consumer.Commit(consumeResult);
                    return consumeResult.Message.Value;
                }
            }
            catch (ConsumeException)
            {
                // ignore transient errors in polling loop
            }
        }

        return default;
    }

    /// <summary>
    /// Consumes all available messages from the topic within the specified timeout.
    /// Continues polling until the timeout expires or maxCount is reached.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for messages.</param>
    /// <param name="maxCount">Maximum number of messages to consume (default 10 for individual test runs).</param>
    /// <param name="cancellationToken">Optional cancellation token to cancel the operation.</param>
    /// <returns>Array of all consumed messages.</returns>
    public TValue[] ConsumeAll(TimeSpan timeout, int maxCount = 10, CancellationToken cancellationToken = default)
    {
        var results = new List<TValue>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested && results.Count < maxCount)
        {
            try
            {
                var consumeResult = _consumer.Consume(timeout);
                if (consumeResult?.Message != null)
                {
                    results.Add(consumeResult.Message.Value);
                }
            }
            catch (ConsumeException)
            {
                // ignore transient errors in polling loop
            }
        }

        return [.. results];
    }

    public void Dispose()
    {
        _consumer.Dispose();
        _schemaClient.Dispose();
    }
}
