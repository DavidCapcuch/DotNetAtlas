using System.Diagnostics;
using Bogus;
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
using Weather.Forecast;

namespace Platform.OutboxRelay.Benchmark.Seed;

/// <summary>
/// Seeds the outbox table with ForecastRequestedEvent messages using Bogus faker,
/// parallel Avro serialization, and Npgsql binary COPY for high-performance bulk insertion.
/// </summary>
public class BenchmarkSeeder
{
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
    /// Seeds the specified number of ForecastRequestedEvent messages into the outbox table.
    /// Uses Bogus for generation, serialization, and Npgsql binary COPY for high-performance insertion.
    /// </summary>
    public async Task SeedAsync(
        int messageCountToSeed,
        CancellationToken ct = default)
    {
        using var _ = SuppressInstrumentationScope.Begin();

        var startTime = DateTime.UtcNow;

        var forecastEvents = GenerateForecastRequestedEvents(messageCountToSeed);
        var outboxMessages = BuildOutboxMessagesFromForecastEvents(forecastEvents);
        await BulkInsertOutboxMessagesAsync(outboxMessages, ct);

        var elapsedSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
        Log.Information("Total seeding time: {Seconds:F2}s ({Rate:N0} msg/s overall)",
            elapsedSeconds, messageCountToSeed / elapsedSeconds);

        var sampleMessage = outboxMessages.First();
        Log.Information("Sample message size: {Size} bytes", sampleMessage.AvroPayload.Length);
    }

    private static List<ForecastRequestedEvent> GenerateForecastRequestedEvents(int count)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        Randomizer.Seed = new Random(420_69);

        Log.Information("Generating {Count:N0} ForecastRequestedEvent messages...", count);

        var forecastRequestedEventFaker = new ForecastRequestedEventFaker();
        var forecastRequestedEvents = forecastRequestedEventFaker.Generate(count);

        var elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;

        Log.Information("Generated {Count:N0} Forecast Avro events in {Seconds:F2}s ({Rate:N0} events/s)",
            count, elapsedSeconds, count / elapsedSeconds);

        return forecastRequestedEvents;
    }

    private List<OutboxMessage> BuildOutboxMessagesFromForecastEvents(
        List<ForecastRequestedEvent> forecastEvents)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var outboxMessages = new List<OutboxMessage>();
        var utcNow = DateTimeOffset.UtcNow;

        foreach (var forecastRequestedEvent in forecastEvents)
        {
            var avroPayload = _universalAvroSerializer.Serialize(forecastRequestedEvent, SerializationContext.Empty);

            outboxMessages.Add(new OutboxMessage
            {
                KafkaKey = forecastRequestedEvent.City,
                AvroPayload = avroPayload,
                Type = typeof(ForecastRequestedEvent).FullName!,
                TopicName = "weather.forecast.requested",
                Headers = null,
                CreatedUtc = utcNow
            });
        }

        var count = forecastEvents.Count;
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
        var fullTableName = $"{schema}.{tableName}";

        await using var writer = await connection.BeginBinaryImportAsync(
            $"COPY \"{fullTableName}\" (\"{nameof(OutboxMessage.KafkaKey)}\", \"{nameof(OutboxMessage.AvroPayload)}\", " +
            $"\"{nameof(OutboxMessage.Type)}\", \"{nameof(OutboxMessage.Headers)}\", \"{nameof(OutboxMessage.CreatedUtc)}\") " +
            "FROM STDIN (FORMAT BINARY)", ct);

        foreach (var message in outboxMessages)
        {
            await writer.StartRowAsync(ct);
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
