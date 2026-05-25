using Confluent.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Payments.Application.Abstractions;
using Payments.Application.Common;
using Payments.Application.Common.Data;
using Payments.Application.Common.Messaging;
using Payments.Infrastructure.ExternalServices.PaymentGateway;
using Payments.Infrastructure.Messaging.Kafka.PaymentCommands;
using Payments.Infrastructure.Persistence.Database;
using Payments.Infrastructure.Persistence.Database.Interceptors;
using Payments.Infrastructure.Persistence.Repositories;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using Xunit;

namespace Payments.IntegrationTests.Common;

/// <summary>
/// xUnit fixture spinning up a throwaway Postgres container per collection and wiring the M5
/// Infrastructure DI graph (PaymentsDbContext + interceptors + repository), the Application
/// composition root (validators + CQRS chain + domain-event dispatcher + outbox publisher
/// domain-event handlers), the transactional outbox with a <see cref="FakeOutboxWriter"/>
/// in place of the real Avro/SchemaRegistry-backed writer, the live
/// <see cref="StubPaymentGateway"/>, and the four saga-command Kafka typed handlers
/// registered as Scoped (matching KafkaFlow's <c>WithHandlerLifetime(InstanceLifetime.Scoped)</c>).
/// Tests resolve the typed handlers from DI and invoke <c>Handle(IMessageContext, T)</c>
/// directly with a synthetic <see cref="FakeKafkaMessageContext"/>; no Kafka or Schema Registry
/// container is started — outbox-emission fidelity is asserted by reading the
/// <see cref="FakeOutboxWriter"/>'s captured messages, mirroring Ordering's M7 precedent at
/// <c>test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs</c>. End-to-end Avro
/// byte-level fidelity lives in the docker-compose smoke (M9).
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Payments",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Payments/Payments.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [PaymentsDbContext.DefaultSchemaName]
        });

    private ServiceProvider _rootServices = null!;

    /// <summary>
    /// Pinned to 2026-04-27 10:00 UTC so assertions on business timestamps don't depend on
    /// wall-clock time.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(
        new DateTimeOffset(2026, 4, 27, 10, 0, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Minimal in-memory IConfiguration satisfies AddOptionsWithValidateOnStart bindings
        // inside Payments.Application's composition root (PaymentsTopicsOptions).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentsTopics:Transactions"] = "payments.transactions",
                ["PaymentsTopics:DltTopicSuffix"] = ".Payments.DLT",
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<TimeProvider>(FakeTime);

        services.AddScoped<DispatchDomainEventsInterceptor>();
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

        services.AddDbContext<PaymentsDbContext>((sp, options) => options
            .UseNpgsql(_dbContainer.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IPaymentsDbContext>(sp => sp.GetRequiredService<PaymentsDbContext>());
        services.AddScoped<IPaymentRepository, PaymentRepository>();

        // Live stub gateway — deterministic ".99" → decline rule per M3 — wrapped in a
        // counting decorator so example-mapping § 2.2 / § 3.3 can assert the saga-retry
        // short-circuit fired before the gateway port was touched.
        services.AddSingleton<StubPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp =>
            new CountingPaymentGateway(sp.GetRequiredService<StubPaymentGateway>()));

        // Replace the real Avro/SchemaRegistry-backed outbox writer with an in-memory fake.
        // Registered BEFORE AddOutbox so the platform's TryAddSingleton<IOutboxWriter> is a
        // no-op — captures topic + key + Avro CLR instance for later assertions without
        // standing up a Schema Registry container.
        services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

        // Application composition root — validators, CQRS handlers, behaviours, domain-event
        // handlers (including the *OutboxPublisherDomainEventHandler set), domain-event
        // dispatcher. Application registers `AddOptions<PaymentsTopicsOptions>()` only; the
        // host is responsible for binding (see ApplicationDependencyInjection.cs:18-21). In
        // the test fixture we configure the section explicitly so outbox publishers receive
        // a non-null Transactions topic.
        services.AddPaymentsApplication();
        // Fail-fast topic-config binding (#220): bind from the in-memory IConfiguration
        // added above (lines 67-74) via AddOptionsWithValidateOnStart + ValidateDataAnnotations
        // so a missing topic key or [Required] violation surfaces at container build time
        // instead of on first publish.
        services.AddOptionsWithValidateOnStart<PaymentsTopicsOptions>()
            .BindConfiguration(PaymentsTopicsOptions.Section)
            .ValidateDataAnnotations();

        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin("Payments");
            outbox.ConfigureSchemaRegistryConfig(opts => opts.Url = "http://mock-schema-registry");
            outbox.ConfigureAvroSerializerConfig(opts =>
            {
                opts.SubjectNameStrategy = SubjectNameStrategy.Record;
                opts.AutoRegisterSchemas = false;
            });
        });

        // Register the four saga-command Kafka typed handler classes as Scoped (matches the
        // production wiring at services/Payments/Payments.Infrastructure/Common/MessagingDependencyInjection.cs).
        // Tests resolve these and invoke Handle(...) directly with a FakeKafkaMessageContext.
        services.AddScoped<AuthorizePaymentCommandKafkaHandler>();
        services.AddScoped<CapturePaymentCommandKafkaHandler>();
        services.AddScoped<VoidPaymentCommandKafkaHandler>();
        services.AddScoped<RequestRefundCommandKafkaHandler>();

        _rootServices = services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a per-test DI scope. Caller disposes.
    /// </summary>
    public IServiceScope CreateScope() => _rootServices.CreateScope();

    /// <summary>Wipes every table in the Payments schema between tests.</summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    /// <summary>
    /// Resolves the singleton <see cref="FakeOutboxWriter"/> so individual tests can
    /// <c>Clear()</c> captured messages or assert on them after driving a handler.
    /// </summary>
    public FakeOutboxWriter GetFakeOutbox() =>
        (FakeOutboxWriter)_rootServices.GetRequiredService<IOutboxWriter>();

    /// <summary>
    /// Resolves the singleton spy decorator over <see cref="StubPaymentGateway"/> so individual
    /// tests can <c>Reset()</c> the call counters between phases or assert that a specific
    /// gateway method was (or was not) invoked.
    /// </summary>
    public CountingPaymentGateway GetGateway() =>
        (CountingPaymentGateway)_rootServices.GetRequiredService<IPaymentGateway>();

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
/// xUnit v3 collection definition — fixtures share one container across all tests in the
/// collection but are isolated from other collections.
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>;
