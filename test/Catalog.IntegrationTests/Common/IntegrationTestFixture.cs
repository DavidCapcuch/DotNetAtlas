using Catalog.Application.Common;
using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Infrastructure.Common;
using Catalog.Infrastructure.Persistence.Database;
using Catalog.Infrastructure.Persistence.Database.Interceptors;
using Confluent.SchemaRegistry;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Respawn;

namespace Catalog.IntegrationTests.Common;

/// <summary>
/// Spins a throwaway Postgres container per collection and wires the M4 DI graph:
/// real <see cref="CatalogDbContext"/>, the <see cref="ICatalogDbContext"/> port
/// binding, the Application layer (validators, CQRS handlers, projection +
/// outbox-publisher domain-event handlers, the M3.5 cycle/path services), the
/// transactional outbox backed by <see cref="FakeOutboxWriter"/>, and a
/// <see cref="FakeTimeProvider"/> pinned to <see cref="Now"/> so OccurredOnUtc
/// + LastUpdatedAtUtc assertions are deterministic across CI runs.
/// </summary>
/// <remarks>
/// Schema comes from the same idempotent V*.sql scripts Flyway runs in compose (#269);
/// Catalog's Initial EF migration was added as part of #269 (Catalog previously relied on
/// EnsureCreatedAsync and had no migrations committed).
/// </remarks>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    /// <summary>Stable test clock — also exposed on <see cref="TimeProvider"/> as a singleton.</summary>
    public static readonly DateTimeOffset Now =
        new(2026, 04, 25, 12, 00, 00, TimeSpan.Zero);

    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Catalog",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Catalog/Catalog.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [CatalogDbContext.DefaultSchemaName]
        });

    private ServiceProvider _rootServices = null!;

    /// <summary>Test-controlled <see cref="FakeTimeProvider"/> — call <c>Advance(...)</c> per scenario.</summary>
    public FakeTimeProvider TimeProvider { get; } = new(Now);

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Minimal in-memory IConfiguration backing the explicit
        // services.Configure<CatalogTopicsOptions>(config.GetSection(...)) call below — outbox
        // publishers resolve topic names through this options instance.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CatalogTopics:CatalogProducts"] = "catalog.products",
                ["CatalogTopics:CatalogCategories"] = "catalog.categories",
                ["CatalogTopics:StockLevelChanged"] = "inventory.stock-level-changed",
                ["CatalogTopics:DltTopicSuffix"] = ".Catalog.DLT",
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<TimeProvider>(TimeProvider);

        // Mirror production wiring (PersistenceDependencyInjection.cs) so projection +
        // outbox-publisher domain-event handlers run inside the same SaveChangesAsync as
        // the aggregate write — the CQRS-on-Postgres atomicity catalog.md § 9 promises.
        // Without these the DispatchDomainEventsInterceptor never fires and product_search_view
        // stays empty regardless of what the command handlers do.
        services.AddScoped<DispatchDomainEventsInterceptor>();
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

        // Real DbContext bypassing Catalog.Infrastructure.AddDatabase (which binds an
        // EfCoreOptions section + production retry knobs not material for tests).
        services.AddDbContext<CatalogDbContext>((sp, options) => options
            .UseNpgsql(_dbContainer.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .UseExceptionProcessor()
            .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<ICatalogDbContext>(sp => sp.GetRequiredService<CatalogDbContext>());

        // Application composition root: validators, CQRS handlers, domain-event dispatcher
        // (which wires projection + outbox publishers), and the M3.5 ICategoryAncestryService +
        // ICategoryPathService. Note: AddCatalogApplication only does AddOptions<CatalogTopicsOptions>()
        // without binding — production wires the binding in Catalog.Infrastructure's
        // MessagingDependencyInjection (which this fixture intentionally skips because it owns the
        // Kafka consumer wiring as well). Bind separately below so outbox publishers resolve real
        // topic names instead of nulls.
        services.AddCatalogApplication();
        // Fail-fast topic-config binding (#220): swap services.Configure<> for
        // AddOptionsWithValidateOnStart + ValidateDataAnnotations so a missing topic
        // key or [Required] violation surfaces at container build time instead of on
        // first publish.
        services.AddOptionsWithValidateOnStart<CatalogTopicsOptions>()
            .BindConfiguration(CatalogTopicsOptions.Section)
            .ValidateDataAnnotations();

        // Replace IOutboxWriter with FakeOutboxWriter BEFORE AddOutbox — the platform's
        // TryAddSingleton respects the prior registration so we bypass the Schema Registry
        // round-trip. Mirrors Basket M6 + Inventory M4.
        services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin(MessagingDependencyInjection.KafkaProducerOrigin);
            outbox.ConfigureSchemaRegistryConfig(opts => opts.Url = "http://mock-schema-registry");
            outbox.ConfigureAvroSerializerConfig(opts =>
            {
                opts.SubjectNameStrategy = SubjectNameStrategy.Record;
                opts.AutoRegisterSchemas = false;
            });
        });

        _rootServices = services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Creates a per-test DI scope; caller disposes.</summary>
    public IServiceScope CreateScope() => _rootServices.CreateScope();

    /// <summary>Connection string for tests that bypass the DbContext.</summary>
    public string ConnectionString => _dbContainer.ConnectionString;

    /// <summary>Wipes every table in the Catalog schema between tests.</summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    public async ValueTask DisposeAsync()
    {
        if (_rootServices is not null)
        {
            await _rootServices.DisposeAsync();
        }

        await _dbContainer.DisposeAsync();
    }
}
