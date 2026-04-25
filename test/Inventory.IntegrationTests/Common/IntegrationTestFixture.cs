using Confluent.SchemaRegistry;
using EntityFramework.Exceptions.PostgreSQL;
using Inventory.Application.Common;
using Inventory.Application.Common.Data;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.Infrastructure.Messaging.Kafka.StockInit;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.Infrastructure.Persistence.EventStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Testcontainers.PostgreSql;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Spins a throwaway Postgres container per collection and wires the full
/// M4+M5 DI graph: <see cref="InventoryDbContext"/>, the event-store
/// repository, the Application layer (validators, CQRS handlers, domain-
/// event handlers + dispatcher), the transactional outbox with a fake
/// <see cref="IOutboxWriter"/>, the <see cref="IInventoryDbContext"/>
/// port binding, plus M5's 5 Kafka typed-handler classes
/// (Reserve/Confirm/Release saga commands + ProductCreated /
/// OrderCancelled cross-BC events). The KafkaFlow cluster itself is NOT
/// booted — tests resolve the typed handlers from DI and invoke
/// <c>Handle(IMessageContext, T)</c> directly with a synthetic
/// <see cref="FakeKafkaMessageContext"/>, matching Ordering's M5
/// precedent at
/// <c>test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs:19-20</c>.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Inventory")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private ServiceProvider _rootServices = null!;

    public async ValueTask InitializeAsync()
    {
        await _pgContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Minimal in-memory IConfiguration satisfies
        // AddOptionsWithValidateOnStart<TopicsOptions>().BindConfiguration(...)
        // inside Inventory.Application's composition root.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Topics:InventoryStockEvents"] = "inventory.stock-events",
                ["Topics:InventoryReservations"] = "inventory.reservations",
                ["Topics:DltTopicSuffix"] = ".Inventory.DLT",
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddDbContext<InventoryDbContext>(options => options
            .UseNpgsql(_pgContainer.GetConnectionString(), npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .UseExceptionProcessor());

        // Event-store + port bindings.
        services.AddScoped<EventStoreRepository>();
        services.AddScoped<IEventStore>(sp => sp.GetRequiredService<EventStoreRepository>());
        services.AddScoped<IInventoryDbContext>(sp => sp.GetRequiredService<InventoryDbContext>());

        // Application composition root: validators + CQRS + domain-event
        // handlers + dispatcher + TopicsOptions binding.
        services.AddApplication();

        // Transactional outbox. We replace IOutboxWriter with a fake BEFORE
        // AddOutbox so the platform's TryAddSingleton skips its OutboxWriter
        // (which would call Schema Registry at write time). The fake inserts
        // the OutboxMessage row directly with topic + key + CLR type preserved
        // — enough for M4 to assert "the right message lands in the right
        // topic" without standing up a Schema Registry container. End-to-end
        // Avro byte-level fidelity is validated in M7 alongside the Kafka
        // consumer wiring (matching the Ordering BC's M4 precedent of
        // outbox-publishers-without-broker).
        services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin("Inventory");
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

        // M5: register the 5 Kafka typed-handler classes as Scoped (matches
        // KafkaFlow's WithHandlerLifetime(InstanceLifetime.Scoped)). Tests
        // resolve these and invoke Handle(...) directly with a synthetic
        // FakeKafkaMessageContext.
        services.AddScoped<ReserveStockCommandKafkaHandler>();
        services.AddScoped<ConfirmReservationCommandKafkaHandler>();
        services.AddScoped<ReleaseReservationCommandKafkaHandler>();
        services.AddScoped<ProductCreatedEventKafkaHandler>();
        services.AddScoped<OrderCancelledEventKafkaHandler>();

        _rootServices = services.BuildServiceProvider();

        // Apply EF migrations once per fixture lifetime.
        await using var migrationScope = _rootServices.CreateAsyncScope();
        var dbContext = migrationScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a per-test DI scope; caller disposes.</summary>
    public IServiceScope CreateScope() => _rootServices.CreateScope();

    /// <summary>Connection string for tests that bypass the DbContext (e.g. raw SQL pre-staging).</summary>
    public string ConnectionString => _pgContainer.GetConnectionString();

    public async ValueTask DisposeAsync()
    {
        if (_rootServices is not null)
        {
            await _rootServices.DisposeAsync();
        }

        await _pgContainer.DisposeAsync();
    }
}
