using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.Test.Framework.Database;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

/// <summary>
/// Test collection for saga integration tests sharing the fixture.
/// </summary>
[CollectionDefinition(nameof(SagaTestCollection))]
public sealed class SagaTestCollection : ICollectionFixture<SagaIntegrationTestFixture>;

/// <summary>
/// Integration test fixture for saga tests using real SQL Server container.
/// Provides MassTransit test harness with EF Core saga persistence.
/// </summary>
public sealed class SagaIntegrationTestFixture : IAsyncLifetime
{
    private readonly SqlServerTestContainer _dbContainer;
    private Respawner _respawner = null!;

    public ServiceProvider ServiceProvider { get; private set; } = null!;
    public ITestHarness TestHarness { get; private set; } = null!;
    public IInjectableTestOutputSink TestOutputSink { get; } = new InjectableTestOutputSink();

    public SagaIntegrationTestFixture()
    {
        // Get the path to the saga migrations folder (empty for now, EF Core will create schema)
        var migrationsPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "db", "saga-migrations");

        // Create empty migrations folder if it doesn't exist
        Directory.CreateDirectory(migrationsPath);

        _dbContainer = new SqlServerTestContainer(
            databaseName: "SagaTest",
            flywayMigrationsPath: migrationsPath,
            new RespawnerOptions
            {
                SchemasToInclude = [SubscriptionSagaDbContext.DefaultSchemaName]
            });
    }

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var services = new ServiceCollection();

        // Configure logging with injectable test output
        services.AddSingleton<IInjectableTestOutputSink>(TestOutputSink);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.InjectableTestOutput(TestOutputSink)
                .Enrich.FromLogContext()
                .CreateLogger(), true);
        });

        // Load configuration from appsettings.Testing.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Testing.json", optional: false)
            .Build();

        // Configure saga options from configuration
        services.Configure<SagaOptions>(configuration.GetSection(SagaOptions.Section));
        var sagaOptions = configuration.GetSection(SagaOptions.Section).Get<SagaOptions>()!;
        services.AddSingleton(Options.Create(sagaOptions));

        // Register TimeProvider for saga
        services.AddSingleton(TimeProvider.System);

        // Configure EF Core with SQL Server container
        services.AddDbContext<SubscriptionSagaDbContext>(options =>
            options.UseSqlServer(_dbContainer.ConnectionString));

        // Configure MassTransit test harness with EF Core repository for all sagas
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    r.ExistingDbContext<SubscriptionSagaDbContext>();
                });

            cfg.AddSagaStateMachine<AlertSubscriptionExtensionSaga, AlertSubscriptionExtensionSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    r.ExistingDbContext<SubscriptionSagaDbContext>();
                });

            cfg.AddSagaStateMachine<PaymentProcessingSaga, PaymentProcessingSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    r.ExistingDbContext<SubscriptionSagaDbContext>();
                });
        });

        // Configure OpenTelemetry for test tracing (TestCaseTracer uses this)
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource(DotNetAtlasInstrumentation.ActivitySourceName)
                .AddSource("*"));

        ServiceProvider = services.BuildServiceProvider(true);

        // Create database schema using EF Core (since we don't have Flyway migrations for saga)
        using (var scope = ServiceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SubscriptionSagaDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        // Create respawner for the saga schema
        _respawner = await Respawner.CreateAsync(_dbContainer.ConnectionString, new RespawnerOptions
        {
            SchemasToInclude = [SubscriptionSagaDbContext.DefaultSchemaName]
        });

        // Start the test harness
        TestHarness = ServiceProvider.GetRequiredService<ITestHarness>();
        await TestHarness.Start();
    }

    public Task ResetDatabaseAsync() => _respawner.ResetAsync(_dbContainer.ConnectionString);

    public async ValueTask DisposeAsync()
    {
        if (TestHarness != null)
        {
            await TestHarness.Stop();
        }

        if (ServiceProvider != null)
        {
            await ServiceProvider.DisposeAsync();
        }

        await _dbContainer.DisposeAsync();
    }
}
