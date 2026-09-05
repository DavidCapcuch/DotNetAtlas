using System.Diagnostics;
using Bogus;
using Catalog.Products;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry;
using Platform.Avro.UniversalSerDes;
using Platform.OutboxRelay.WorkerService.OutboxRelay;
using Platform.ReliableMessaging.Outbox.Core;
using Serilog;

namespace Platform.OutboxRelay.Benchmark.Seed;

/// <summary>
/// Seeds the outbox table with ProductCreatedEvent messages using Bogus faker,
/// Avro serialization, and Npgsql binary COPY for high-performance bulk insertion.
/// </summary>
public class BenchmarkSeeder
{
    /// <summary>
    /// The fixture provisions this on the broker, which does not auto-create topics.
    /// </summary>
    public const string SeededTopicName = "catalog.products";

    private readonly IDbContextFactory<OutboxDbContext> _dbContextFactory;
    private readonly UniversalAvroSerializer _universalAvroSerializer;

    public BenchmarkSeeder(IServiceProvider services)
    {
        _dbContextFactory = services.GetRequiredService<IDbContextFactory<OutboxDbContext>>();

        _universalAvroSerializer = new UniversalAvroSerializer(services.GetRequiredService<ISchemaRegistryClient>(),
            new AvroSerializerConfig
            {
                AutoRegisterSchemas = true,
                SubjectNameStrategy = SubjectNameStrategy.Record,
                NormalizeSchemas = true
            });
    }

    /// <summary>
    /// Seeds the specified number of ProductCreatedEvent messages into the outbox table.
    /// Uses Bogus for generation, serialization, and Npgsql binary COPY for high-performance insertion.
    /// </summary>
    public async Task SeedAsync(
        int messageCountToSeed,
        CancellationToken ct = default)
    {
        using var _ = SuppressInstrumentationScope.Begin();

        var startTime = DateTime.UtcNow;

        var productEvents = GenerateProductCreatedEvents(messageCountToSeed);
        var outboxMessages = BuildOutboxMessagesFromProductEvents(productEvents);
        await BulkInsertOutboxMessagesAsync(outboxMessages, ct);

        var elapsedSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
        Log.Information("Total seeding time: {Seconds:F2}s ({Rate:N0} msg/s overall)",
            elapsedSeconds, messageCountToSeed / elapsedSeconds);

        var sampleMessage = outboxMessages.First();
        Log.Information("Sample message size: {Size} bytes", sampleMessage.AvroPayload.Length);
    }

    private static List<ProductCreatedEvent> GenerateProductCreatedEvents(int count)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        Randomizer.Seed = new Random(420_69);

        Log.Information("Generating {Count:N0} ProductCreatedEvent messages...", count);

        var productCreatedEventFaker = new ProductCreatedEventFaker();
        var productCreatedEvents = productCreatedEventFaker.Generate(count);

        var elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;

        Log.Information("Generated {Count:N0} Product Avro events in {Seconds:F2}s ({Rate:N0} events/s)",
            count, elapsedSeconds, count / elapsedSeconds);

        return productCreatedEvents;
    }

    private List<OutboxMessage> BuildOutboxMessagesFromProductEvents(
        List<ProductCreatedEvent> productEvents)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var outboxMessages = new List<OutboxMessage>();
        var utcNow = DateTimeOffset.UtcNow;

        foreach (var productCreatedEvent in productEvents)
        {
            var avroPayload = _universalAvroSerializer.Serialize(productCreatedEvent, SerializationContext.Empty);

            outboxMessages.Add(new OutboxMessage
            {
                KafkaKey = productCreatedEvent.ProductId.ToString(),
                AvroPayload = avroPayload,
                Type = typeof(ProductCreatedEvent).FullName!,
                TopicName = SeededTopicName,
                Headers = null,
                CreatedUtc = utcNow
            });
        }

        var count = productEvents.Count;
        var elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        Log.Information("Serialized {Count:N0} messages in {Seconds:F2}s ({Rate:N0} msg/s)",
            count, elapsedSeconds, count / elapsedSeconds);

        return outboxMessages;
    }

    private async Task BulkInsertOutboxMessagesAsync(
        List<OutboxMessage> outboxMessages,
        CancellationToken ct)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var count = outboxMessages.Count;
        Log.Information("Starting Npgsql COPY of {Count:N0} messages...", count);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);
        await using var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        var tableMetadata = dbContext.OutboxMessages.EntityType;
        var tableName = tableMetadata.GetTableName();
        var schema = tableMetadata.GetSchema();

        // Columns are snake_case (EFCore.NamingConventions on OutboxDbContext); id is identity, so it is
        // omitted and generated by Postgres. topic_name is NOT NULL and is what the relay routes on.
        // Quote schema and table separately - "schema.table" would be parsed as a single identifier.
        await using var writer = await connection.BeginBinaryImportAsync(
            $"COPY \"{schema}\".\"{tableName}\" " +
            "(topic_name, kafka_key, avro_payload, type, headers, created_utc) " +
            "FROM STDIN (FORMAT BINARY)", ct);

        foreach (var message in outboxMessages)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(message.TopicName, ct);
            await writer.WriteAsync(message.KafkaKey, ct);
            await writer.WriteAsync(message.AvroPayload, ct);
            await writer.WriteAsync(message.Type, ct);
            await writer.WriteAsync(message.Headers ?? (object)DBNull.Value, ct);
            await writer.WriteAsync(message.CreatedUtc, ct);
        }

        await writer.CompleteAsync(ct);

        var elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        Log.Information("Npgsql COPY completed in {Seconds:F2}s ({Rate:N0} msg/s)",
            elapsedSeconds, count / elapsedSeconds);
    }
}
