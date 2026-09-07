using System.Diagnostics;
using Avro.Specific;
using Confluent.SchemaRegistry;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Platform.OutboxRelay.WorkerService.Common.Config;
using Platform.OutboxRelay.WorkerService.OutboxRelay;
using Platform.OutboxRelay.WorkerService.OutboxRelay.Config;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Kafka.Config;
using Respawn;

namespace Platform.OutboxRelay.WorkerService.IntegrationTests;

/// <summary>
/// One row to write through the real <see cref="IOutboxWriter"/>.
/// </summary>
/// <param name="TopicName">Value of <see cref="OutboxMessage.TopicName"/>, the relay's only routing input.</param>
/// <param name="KafkaKey">Partition key.</param>
/// <param name="Event">The Avro integration event; serialized against the live Schema Registry.</param>
public sealed record OutboxRow(string TopicName, string? KafkaKey, ISpecificRecord Event);

/// <summary>
/// One Postgres, one Kafka, one Schema Registry and one running OutboxRelay host for the whole
/// assembly.
/// </summary>
/// <remarks>
/// Tests isolate on a topic minted per test (see <see cref="CreateTopicAsync"/>) rather than by
/// resetting the database: the relay polls continuously against a shared table, so a mid-run reset
/// races whatever it is draining.
/// </remarks>
[DisableWafCache]
public sealed class OutboxPublishPathFixture : AppFixture<Platform.OutboxRelay.WorkerService.Program>
{
    /// <summary>Stamped onto every row's <c>origin</c> header, and asserted on the wire.</summary>
    public const string MessageOrigin = "Platform_OutboxRelay_IntegrationTests";

    /// <summary>Schema provisioned by Seed/V001, and the literal <c>OutboxRelay:SchemaName</c> below.</summary>
    private const string OutboxSchemaName = "outbox_relay_tests";

    /// <summary>The platform default table name. Matches V001 and <c>OutboxRelay:TableName</c> below.</summary>
    private const string OutboxTableName = "outbox_messages";

    // Composed from the constants above rather than interpolated at run time, so the analyzer can
    // see there is no dynamic input in them.
    private const string BlockDeletesSql =
        "CREATE OR REPLACE FUNCTION " + OutboxSchemaName + ".block_delete() RETURNS trigger AS $$ " +
        "BEGIN RAISE EXCEPTION 'outbox delete blocked by test'; END; $$ LANGUAGE plpgsql; " +
        "CREATE OR REPLACE TRIGGER block_outbox_delete BEFORE DELETE ON " +
        OutboxSchemaName + "." + OutboxTableName +
        " FOR EACH ROW EXECUTE FUNCTION " + OutboxSchemaName + ".block_delete();";

    private const string AllowDeletesSql =
        "DROP TRIGGER IF EXISTS block_outbox_delete ON " + OutboxSchemaName + "." + OutboxTableName + ";";

    /// <summary>How long a test waits for the relay, generously against its 250ms poll — a loaded
    /// machine is what makes this slow, and an expiring deadline is a named failure.</summary>
    public static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "OutboxPublishPath",
        sqlScriptsMigrationsPath: Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(),
            "platform",
            "Platform.OutboxRelay.WorkerService.IntegrationTests",
            "Seed"),
        new RespawnerOptions
        {
            SchemasToInclude = [OutboxSchemaName]
        });

    private readonly KafkaTestContainer _kafkaContainer = new();

    /// <summary>
    /// Stands in for the production OpenTelemetry pipeline. Without a listener, every
    /// <c>ActivitySource.CreateActivity</c> in the process returns null — the writer would stamp no
    /// <c>traceparent</c> on the row, and
    /// <c>KafkaProducerDiagnostics.StartProduceActivityAndStampTraceContext</c> would pass the row's
    /// headers through untouched instead of replacing them with the produce span. Both are exactly
    /// what the trace test distinguishes, so it would pass for the wrong reason.
    /// </summary>
    private readonly ActivityListener _activityListener = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public KafkaOptions KafkaOptions { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        ActivitySource.AddActivityListener(_activityListener);

        // Sequentially, not Task.WhenAll: concurrent Docker.DotNet calls interleave on the shared
        // chunked read stream over the Windows named pipe. Same reason as
        // test/Catalog.IntegrationTests/Common/IntegrationTestFixture.cs.
        await _dbContainer.StartAsync();
        await _kafkaContainer.StartAsync();

        KafkaOptions = _kafkaContainer.KafkaOptions;
    }

    protected override IHost ConfigureAppHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Outbox)}",
                    _dbContainer.ConnectionString)
                .UseSetting($"{KafkaProducerOptions.Section}:BootstrapServers",
                    _kafkaContainer.KafkaOptions.BrokersFlat)
                // The relay's producer is disposed during service-provider disposal, after the
                // containers below are gone, and librdkafka blocks there for this long against an
                // unreachable broker. The shipped 300s would outlast the test runner's own timeout.
                .UseSetting($"{KafkaProducerOptions.Section}:MessageTimeoutMs", "5000")
                // Required and deliberately undefaulted in appsettings.json, so the host will not
                // boot without them.
                .UseSetting($"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.SchemaName)}",
                    OutboxSchemaName)
                .UseSetting($"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.TableName)}",
                    OutboxTableName)
                // Overrides of the shipped values, so a test waits on Kafka rather than on the
                // relay's timer and teardown cannot sit on the 60s production shutdown budget.
                .UseSetting($"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.PollingIntervalMs)}", "250")
                // Comfortably above MessageTimeoutMs, so a delivery failure surfaces as a failed
                // delivery report inside the flush rather than as a flush timeout — the flush
                // short-circuits the batch, which would hide the partial-failure path from tests.
                .UseSetting($"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.FlushTimeoutMs)}", "15000")
                .UseSetting($"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.ShutdownTimeoutMs)}", "20000")
                // No collector is listening; left set, every export attempt waits out its timeout.
                .UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        });

        return base.ConfigureAppHost(builder);
    }

    protected override void ConfigureApp(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
            // The relay only reads rows, so the host has no writer. Registering the real one here
            // through the platform's own entry point is what makes this an end-to-end test rather
            // than one that hand-crafts its own rows.
            services.AddOutbox(outbox => outbox
                .ConfigureMessageOrigin(MessageOrigin)
                .ConfigureSchemaRegistryConfig(config => config.Url = KafkaOptions.SchemaRegistry.Url)
                .ConfigureAvroSerializerConfig(config =>
                {
                    // Matches KafkaTestContainer's consumer-side defaults: the subject is the Avro
                    // record's full name, so it resolves independently of the topic the row routes to.
                    config.AutoRegisterSchemas = true;
                    config.SubjectNameStrategy = SubjectNameStrategy.Record;
                    config.NormalizeSchemas = true;
                })));

    /// <summary>
    /// Provisions a topic unique to the calling test and returns its name.
    /// </summary>
    /// <remarks>
    /// Per test, not per class. Consumers read from the earliest offset on a fresh group and the
    /// topics are never reset, so a topic shared by two tests would serve the first test's message
    /// to the second — which an assertion that only checks for arrival would accept.
    /// </remarks>
    /// <param name="purpose">Short slug naming what the test asserts; appears in the topic name.</param>
    public async Task<string> CreateTopicAsync(string purpose)
    {
        // The broker does not auto-create topics.
        var topic = $"platform.outbox-tests.{purpose}-{Guid.NewGuid():N}";
        await _kafkaContainer.CreateKafkaTopicsAsync([topic]);

        return topic;
    }

    /// <summary>
    /// Writes <paramref name="rows"/> through the real <see cref="IOutboxWriter"/> in one
    /// <c>SaveChangesAsync</c>, and returns the ids the database assigned.
    /// </summary>
    public async Task<IReadOnlyList<long>> WriteOutboxRowsAsync(
        IReadOnlyList<OutboxRow> rows,
        CancellationToken ct)
    {
        var writer = Services.GetRequiredService<IOutboxWriter>();
        var factory = Services.GetRequiredService<IDbContextFactory<OutboxDbContext>>();

        await using var dbContext = await factory.CreateDbContextAsync(ct);

        foreach (var row in rows)
        {
            writer.AddOutboxMessage(dbContext, row.TopicName, row.KafkaKey, row.Event);
        }

        var written = dbContext.ChangeTracker
            .Entries<OutboxMessage>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        await dbContext.SaveChangesAsync(ct);

        return written.Select(message => message.Id).ToList();
    }

    /// <summary>
    /// Whether any of <paramref name="ids"/> is still in the outbox table. The relay deletes a row
    /// only after the broker acknowledges it, so their absence is this design's delivery receipt —
    /// there is no status column to read.
    /// </summary>
    public async Task<bool> AnyOutboxRowsRemainAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        var factory = Services.GetRequiredService<IDbContextFactory<OutboxDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(ct);

        return await dbContext.OutboxMessages.AnyAsync(message => ids.Contains(message.Id), ct);
    }

    /// <summary>
    /// Removes rows the relay will never drain, so one test cannot strand the shared relay.
    /// </summary>
    /// <remarks>
    /// A row whose delivery keeps failing is re-selected on every poll and, being the lowest id,
    /// takes the rest of its batch out of the delete set with it — so a test that leaves one behind
    /// stops the relay publishing anything for every test that follows.
    /// </remarks>
    public async Task DeleteOutboxRowsAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        var factory = Services.GetRequiredService<IDbContextFactory<OutboxDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(ct);

        await dbContext.OutboxMessages.Where(message => ids.Contains(message.Id)).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// A consumer on its own group, reading <paramref name="topic"/> from the earliest offset, so it
    /// sees a message produced before it subscribed.
    /// </summary>
    public KafkaTestConsumer<TValue> CreateConsumer<TValue>(string topic)
        where TValue : class, ISpecificRecord =>
        new(KafkaOptions.BrokersFlat, KafkaOptions.SchemaRegistry.Url, topic);

    /// <summary>
    /// Makes every DELETE on the outbox table raise, so a test can hold the relay's delete open for
    /// as long as it needs. Always pair with <see cref="AllowOutboxDeletesAsync"/> in a finally.
    /// </summary>
    public async Task BlockOutboxDeletesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_dbContainer.ConnectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(BlockDeletesSql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task AllowOutboxDeletesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_dbContainer.ConnectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(AllowDeletesSql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    protected override async ValueTask TearDownAsync()
    {
        // Stop the relay before its broker and database vanish. The worker polls both every 250ms,
        // and OutboxMetricsCollector queries the outbox table on its own timer; letting the
        // containers go first puts every one of them through a dependency-loss path no test covers.
        var lifetime = Services.GetService<IHostApplicationLifetime>();
        if (lifetime is not null)
        {
            lifetime.StopApplication();
            await WaitForStoppedAsync(lifetime.ApplicationStopped);
        }

        _activityListener.Dispose();

        await _dbContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }

    /// <summary>
    /// Returns as soon as the host has stopped, and otherwise gives up — so a host that will not
    /// stop still lets teardown dispose the containers instead of hanging the run with no stack
    /// trace.
    /// </summary>
    private static async Task WaitForStoppedAsync(CancellationToken applicationStopped)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), applicationStopped);
        }
        catch (OperationCanceledException)
        {
            // The host stopped, which is what this was waiting for.
        }
    }
}

/// <summary>
/// One collection for the assembly, so every test class shares the single fixture above.
/// </summary>
public sealed class OutboxPublishPathTestCollection : TestCollection<OutboxPublishPathFixture>;
