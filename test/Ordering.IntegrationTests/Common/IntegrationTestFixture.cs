using Confluent.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Ordering.Application.Common;
using Ordering.Application.Common.Data;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.Infrastructure.Persistence.Database.Interceptors;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using Xunit;

namespace Ordering.IntegrationTests.Common;

/// <summary>
/// xUnit fixture spinning up a throwaway Postgres container per collection
/// and wiring the full M7 DI graph: <see cref="OrderingDbContext"/> + its
/// interceptors, the Application composition root (validators + CQRS chain
/// + domain-event dispatcher + outbox publisher domain-event handlers), the
/// transactional outbox with a <see cref="FakeOutboxWriter"/> in place of
/// the real Avro/SchemaRegistry-backed writer, plus the four saga-command
/// Kafka typed handlers registered as Scoped (matching KafkaFlow's
/// <c>WithHandlerLifetime(InstanceLifetime.Scoped)</c>). Tests resolve the
/// typed handlers from DI and invoke <c>Handle(IMessageContext, T)</c>
/// directly with a synthetic <see cref="FakeKafkaMessageContext"/>; no
/// Kafka or Schema-Registry container is started — outbox-emission
/// fidelity is asserted by reading the <see cref="FakeOutboxWriter"/>'s
/// captured messages, mirroring Inventory's M5 precedent at
/// <c>test/Inventory.IntegrationTests/Common/IntegrationTestFixture.cs:34-122</c>.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Ordering",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Ordering/Ordering.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [OrderingDbContext.DefaultSchemaName]
        });

    private ServiceProvider _rootServices = null!;

    /// <summary>
    /// Pinned to 2026-04-23 10:00 UTC so assertions on <c>CreatedAtUtc</c>
    /// etc. do not depend on wall-clock time.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(
        new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Minimal in-memory IConfiguration satisfies
        // AddOptionsWithValidateOnStart<TopicsOptions>().BindConfiguration(...)
        // inside Ordering.Application's composition root. Saga-command topic
        // (ordering.order-commands) is consumer-side only and therefore not
        // listed in TopicsOptions.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Topics:OrderingOrders"] = "ordering.orders",
                ["Topics:DltTopicSuffix"] = ".Ordering.DLT",
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<TimeProvider>(FakeTime);

        services.AddScoped<DispatchDomainEventsInterceptor>();
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

        services.AddDbContext<OrderingDbContext>((sp, options) => options
            .UseNpgsql(_dbContainer.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IOrderingDbContext>(sp => sp.GetRequiredService<OrderingDbContext>());

        // Replace the real Avro/SchemaRegistry-backed outbox writer with an
        // in-memory fake. Registered BEFORE AddOutbox so the platform's
        // TryAddSingleton<IOutboxWriter> is a no-op — the fake captures the
        // outbox row's topic + key + Avro CLR instance for later assertions
        // without standing up a Schema Registry container. End-to-end Avro
        // byte-level fidelity lives in the docker-compose smoke (M8).
        services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

        // Application composition root — validators, CQRS handlers (including
        // MarkOrderStockReserved + MarkOrderPaymentCompleted via
        // AddCqrsHandlersFromAssembly), CQRS behaviours, domain-event
        // handlers (including the *OutboxPublisherDomainEventHandler set,
        // which fan internal events out to the FakeOutboxWriter on
        // SaveChanges), domain-event dispatcher, and TopicsOptions binding.
        services.AddApplication();

        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin("Ordering");
            outbox.ConfigureSchemaRegistryConfig(opts =>
            {
                opts.Url = "http://mock-schema-registry";
            });
            outbox.ConfigureAvroSerializerConfig(opts =>
            {
                opts.SubjectNameStrategy = SubjectNameStrategy.Record;
                opts.AutoRegisterSchemas = false;
            });
        });

        // M7: register the four saga-command Kafka typed handler classes as
        // Scoped (matches KafkaFlow's WithHandlerLifetime(InstanceLifetime.Scoped)
        // in production wiring at
        // services/Ordering/Ordering.Infrastructure/Common/MessagingDependencyInjection.cs:94-99).
        // Tests resolve these and invoke Handle(...) directly with a
        // FakeKafkaMessageContext.
        services.AddScoped<CreateOrderCommandKafkaHandler>();
        services.AddScoped<ConfirmOrderCommandKafkaHandler>();
        services.AddScoped<CancelOrderCommandKafkaHandler>();
        services.AddScoped<MarkOrderFailedCommandKafkaHandler>();

        _rootServices = services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a per-test DI scope. Caller disposes.
    /// </summary>
    public IServiceScope CreateScope() => _rootServices.CreateScope();

    /// <summary>Wipes every table in the Ordering schema between tests.</summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    /// <summary>
    /// Resolves the singleton <see cref="FakeOutboxWriter"/> so individual
    /// tests can <c>Clear()</c> captured messages or assert on them after
    /// driving a handler.
    /// </summary>
    public FakeOutboxWriter GetFakeOutbox() =>
        (FakeOutboxWriter)_rootServices.GetRequiredService<IOutboxWriter>();

    public async ValueTask DisposeAsync()
    {
        if (_rootServices is not null)
        {
            await _rootServices.DisposeAsync();
        }

        await _dbContainer.DisposeAsync();
    }
}

/// <summary>
/// xUnit v3 collection definition — fixtures share one container across all
/// tests in the collection but are isolated from other collections.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;
