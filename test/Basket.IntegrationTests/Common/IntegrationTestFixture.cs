using Basket.Application.Abstractions;
using Basket.Application.Common;
using Basket.Application.Common.Data;
using Basket.Infrastructure.Common;
using Basket.Infrastructure.Persistence.Database;
using Confluent.SchemaRegistry;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Testcontainers.PostgreSql;

namespace Basket.IntegrationTests.Common;

/// <summary>
/// Spins a throwaway Postgres container per collection and wires the M6 DI
/// graph: real <see cref="BasketDbContext"/>, the
/// <see cref="IBasketDbContext"/> port binding, the Application layer
/// (validators, CQRS handlers, domain-event dispatcher), and the transactional
/// outbox backed by <see cref="FakeOutboxWriter"/>. <see cref="IBasketRepository"/>
/// and <see cref="IProductCatalogQueryPort"/> remain NSubstitute-stubbed —
/// M6's focus is the SQL outbox roundtrip, not Redis or Catalog HTTP.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Stable test clock — shared with tests that want to assert on
    /// deterministic timestamps without re-importing FakeTimeProvider.
    /// </summary>
    public static readonly DateTimeOffset Now =
        new(2026, 04, 25, 12, 00, 00, TimeSpan.Zero);

    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Basket")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private ServiceProvider _rootServices = null!;

    /// <summary>
    /// Test-controlled <see cref="IBasketRepository"/>. Tests configure return
    /// values per-scenario; the fixture exposes the substitute so they don't
    /// have to re-resolve from DI.
    /// </summary>
    public IBasketRepository Repository { get; } = Substitute.For<IBasketRepository>();

    /// <summary>
    /// Test-controlled <see cref="IProductCatalogQueryPort"/>. Registered
    /// because Application DI requires it; not exercised by Checkout
    /// (snapshot-validation runs at AddItem time, not Checkout).
    /// </summary>
    public IProductCatalogQueryPort Catalog { get; } = Substitute.For<IProductCatalogQueryPort>();

    public async ValueTask InitializeAsync()
    {
        await _pgContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Minimal in-memory IConfiguration satisfies
        // AddOptionsWithValidateOnStart<TopicsOptions>().BindConfiguration(...)
        // inside Basket.Application's composition root.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Topics:BasketSessions"] = "basket.sessions",
                ["Topics:DltTopicSuffix"] = ".Basket.DLT",
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        // Real DbContext bypassing AddDatabase (which expects ConfigurationManager
        // + an EfCoreOptions section). M6 tests assert on the outbox table
        // directly, so the production EF retry/splitting knobs are not material.
        services.AddDbContext<BasketDbContext>(options => options
            .UseNpgsql(_pgContainer.GetConnectionString(), npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", BasketDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .UseExceptionProcessor());

        services.AddScoped<IBasketDbContext>(sp => sp.GetRequiredService<BasketDbContext>());

        // Application composition root: validators + CQRS handlers + domain-event
        // dispatcher + TopicsOptions binding.
        services.AddApplication();

        // Test seams.
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddSingleton(Repository);
        services.AddSingleton(Catalog);

        // Replace IOutboxWriter with FakeOutboxWriter BEFORE AddOutbox — the
        // platform's TryAddSingleton respects the prior registration so we
        // bypass the Schema Registry round-trip. Mirrors the Inventory M4
        // fixture pattern.
        services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

        services.AddOutbox(outbox =>
        {
            // Use the production const so a future rename of
            // MessagingDependencyInjection.KafkaProducerOrigin propagates here
            // and the fixture can't silently drift from production wiring.
            outbox.ConfigureMessageOrigin(MessagingDependencyInjection.KafkaProducerOrigin);
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

        _rootServices = services.BuildServiceProvider(validateScopes: true);

        // Apply EF migrations once per fixture lifetime.
        await using var migrationScope = _rootServices.CreateAsyncScope();
        var dbContext = migrationScope.ServiceProvider.GetRequiredService<BasketDbContext>();
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a per-test DI scope; caller disposes.</summary>
    public IServiceScope CreateScope() => _rootServices.CreateScope();

    /// <summary>Connection string for tests that bypass the DbContext.</summary>
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
