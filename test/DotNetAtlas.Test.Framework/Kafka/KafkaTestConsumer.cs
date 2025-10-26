using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace DotNetAtlas.Test.Framework.Kafka;

public interface IKafkaTestConsumer : IDisposable
{
}

/// <summary>
/// Generic Kafka test consumer for integration tests.
/// Creates a unique consumer group per instance to avoid conflicts.
/// </summary>
/// <typeparam name="TValue">The Avro message type to deserialize.</typeparam>
public sealed class KafkaTestConsumer<TValue> : IKafkaTestConsumer
    where TValue : class, ISpecificRecord
{
    private readonly CachedSchemaRegistryClient _schemaClient;
    private readonly IConsumer<string, TValue> _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaTestConsumer{TValue}"/> class.
    /// </summary>
    /// <param name="bootstrapServers">Kafka bootstrap servers address.</param>
    /// <param name="schemaRegistryUrl">Schema Registry URL.</param>
    /// <param name="topic">Topic to subscribe to.</param>
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
            AutoOffsetReset = AutoOffsetReset.Earliest,
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
    /// <param name="ct">Optional cancellation token to cancel the operation.</param>
    /// <returns>The deserialized message value, or null if no message was received within the timeout.</returns>
    public TValue? ConsumeOne(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(timeout);
                if (consumeResult?.Message != null)
                {
                    return consumeResult.Message.Value;
                }
            }
            catch (ConsumeException e) when (!e.Error.IsFatal)
            {
                // Ignore non-fatal transient errors and retry
            }
            catch (OperationCanceledException)
            {
                // Expected when timeout is reached
            }
        }

        return null;
    }

    /// <summary>
    /// Consumes all available messages from the topic within the specified timeout.
    /// Continues polling until the timeout expires or maxCount is reached.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for messages.</param>
    /// <param name="maxCount">Maximum number of messages to consume (default 10 for individual test runs).</param>
    /// <param name="ct">Optional cancellation token to cancel the operation.</param>
    /// <returns>Array of all consumed messages.</returns>
    public List<TValue> ConsumeMultiple(TimeSpan timeout, int maxCount = 10, CancellationToken ct = default)
    {
        var messages = new List<TValue>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (!cts.IsCancellationRequested && messages.Count < maxCount)
        {
            var message = ConsumeOne(timeout, cts.Token);
            if (message != null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    public void Dispose()
    {
        _consumer.Dispose();
        _schemaClient.Dispose();
    }
}
