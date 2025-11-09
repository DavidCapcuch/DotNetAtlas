using System.Data;
using System.Diagnostics;
using Bogus;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Outbox.Core;
using DotNetAtlas.Outbox.EntityFrameworkCore.Core;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using Serilog;
using Weather.Forecast;

namespace DotNetAtlas.OutboxRelay.Benchmark.Seed;

/// <summary>
/// Seeds the outbox table with ForecastRequestedEvent messages using Bogus faker,
/// parallel Avro serialization, and SqlBulkCopy for high-performance bulk insertion.
/// </summary>
public class BenchmarkSeeder
{
    private readonly IDbContextFactory<OutboxDbContext> _dbContextFactory;
    private readonly AvroSerializer _avroSerializer;

    public BenchmarkSeeder(IServiceProvider services)
    {
        _dbContextFactory = services.GetRequiredService<IDbContextFactory<OutboxDbContext>>();

        _avroSerializer = new AvroSerializer(services.GetRequiredService<ISchemaRegistryClient>(),
            new AvroSerializerConfig
            {
                AutoRegisterSchemas = true,
                SubjectNameStrategy = SubjectNameStrategy.Record,
                NormalizeSchemas = true
            });
    }

    /// <summary>
    /// Seeds the specified number of ForecastRequestedEvent messages into the outbox table.
    /// Uses Bogus for generation, serialization, and SqlBulkCopy for high-performance insertion.
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
            var avroPayload = _avroSerializer.Serialize(forecastRequestedEvent, typeof(ForecastRequestedEvent));

            outboxMessages.Add(new OutboxMessage
            {
                KafkaKey = forecastRequestedEvent.City,
                AvroPayload = avroPayload,
                Type = nameof(ForecastRequestedEvent),
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
        Log.Information("Starting SqlBulkCopy of {Count:N0} messages...", count);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);
        await using var connection = new SqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        // Since we seed 1000s of messages, SqlBulkCopy is preferred over EF Core methods since it's
        // at least 10x faster https://timdeschryver.dev/blog/faster-sql-bulk-inserts-with-csharp
        using var dataTable = new DataTable();

        dataTable.Columns.Add(nameof(OutboxMessage.KafkaKey), typeof(string));
        dataTable.Columns.Add(nameof(OutboxMessage.AvroPayload), typeof(byte[]));
        dataTable.Columns.Add(nameof(OutboxMessage.Type), typeof(string));
        dataTable.Columns.Add(nameof(OutboxMessage.Headers), typeof(string));
        dataTable.Columns.Add(nameof(OutboxMessage.CreatedUtc), typeof(DateTimeOffset));

        foreach (var message in outboxMessages)
        {
            dataTable.Rows.Add(
                message.KafkaKey,
                message.AvroPayload,
                message.Type,
                message.Headers,
                message.CreatedUtc
            );
        }

        var tableMetadata = dbContext.OutboxMessages.EntityType;
        var tableName = tableMetadata.GetTableName();
        var schema = tableMetadata.GetSchema();
        var fullTableName = $"{schema}.{tableName}";

        using var bulkCopy = new SqlBulkCopy(connection);
        bulkCopy.DestinationTableName = fullTableName;
        bulkCopy.BulkCopyTimeout = 0;
        bulkCopy.BatchSize = 10000;
        bulkCopy.ColumnMappings.Add(nameof(OutboxMessage.KafkaKey), nameof(OutboxMessage.KafkaKey));
        bulkCopy.ColumnMappings.Add(nameof(OutboxMessage.AvroPayload), nameof(OutboxMessage.AvroPayload));
        bulkCopy.ColumnMappings.Add(nameof(OutboxMessage.Type), nameof(OutboxMessage.Type));
        bulkCopy.ColumnMappings.Add(nameof(OutboxMessage.Headers), nameof(OutboxMessage.Headers));
        bulkCopy.ColumnMappings.Add(nameof(OutboxMessage.CreatedUtc), nameof(OutboxMessage.CreatedUtc));

        await bulkCopy.WriteToServerAsync(dataTable, ct);

        var elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        Log.Information("SqlBulkCopy completed in {Seconds:F2}s ({Rate:N0} msg/s)",
            elapsedSeconds, count / elapsedSeconds);
    }
}
