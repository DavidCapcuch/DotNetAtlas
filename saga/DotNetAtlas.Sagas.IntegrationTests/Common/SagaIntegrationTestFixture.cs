using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.Sagas.UnitTests.Fakes;
using DotNetAtlas.Test.Framework;
using DotNetAtlas.Test.Framework.Database;
using DotNetAtlas.Test.Framework.Kafka;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
/// Integration test fixture for saga tests using WebApplicationFactory and real SQL Server container.
/// Provides MassTransit test harness with EF Core saga persistence.
/// </summary>
public sealed class SagaIntegrationTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqlServerTestContainer _dbContainer;
    private readonly KafkaTestContainer _kafkaContainer = new();

    public ITestHarness TestHarness { get; private set; } = null!;
    public FakeOutboxWriter FakeOutboxWriter { get; } = new();

    public SagaIntegrationTestFixture()
    {
        var migrationsPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(), "saga", "DotNetAtlas.Sagas", "Persistence", "Database",
            "Migrations", "Flyway");

        _dbContainer = new SqlServerTestContainer(
            databaseName: "Saga",
            flywayMigrationsPath: migrationsPath,
            new RespawnerOptions
            {
                SchemasToInclude = [SagaDbContext.DefaultSchemaName]
            });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment("Testing")
            .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Saga)}", _dbContainer.ConnectionString)
            .UseKafkaaSettings(_kafkaContainer.KafkaOptions)
            .ConfigureServices(services =>
            {
                var testOutputSink = new InjectableTestOutputSink();
                services.AddSingleton<IInjectableTestOutputSink>(testOutputSink);
                services.AddSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .MinimumLevel.Debug()
                        .WriteTo.InjectableTestOutput(testOutputSink)
                        .Enrich.FromLogContext();
                }, true, true);
            })
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<IOutboxWriter>(FakeOutboxWriter);

                services.AddMassTransitTestHarness(cfg =>
                {
                    cfg.AddSagaStateMachine<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>()
                        .EntityFrameworkRepository(r =>
                        {
                            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                            r.ExistingDbContext<SagaDbContext>();
                        });

                    cfg.AddSagaStateMachine<AlertSubscriptionExtensionSaga, AlertSubscriptionExtensionSagaState>()
                        .EntityFrameworkRepository(r =>
                        {
                            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                            r.ExistingDbContext<SagaDbContext>();
                        });

                    cfg.AddSagaStateMachine<PaymentProcessingSaga, PaymentProcessingSagaState>()
                        .EntityFrameworkRepository(r =>
                        {
                            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                            r.ExistingDbContext<SagaDbContext>();
                        });
                });
            });
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_dbContainer.StartAsync(), _kafkaContainer.StartAsync());

        var topics = new[]
        {
            "order.alert-subscriptions", "weather.alert-subscriptions", "weather.alert-subscriptions.commands",
            "finance.payments", "finance.payment-commands"
        };
        await _kafkaContainer.CreateKafkaTopicsAsync(topics);

        TestHarness = Services.GetRequiredService<ITestHarness>();
        await TestHarness.Start();
    }

    public Task ResetDatabaseAsync() => _dbContainer.CleanDataAsync();

    public new async ValueTask DisposeAsync()
    {
        await TestHarness.Stop();
        await base.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }
}
