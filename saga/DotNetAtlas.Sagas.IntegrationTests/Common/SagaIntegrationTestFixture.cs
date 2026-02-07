using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.Test.Framework;
using DotNetAtlas.Test.Framework.Database;
using DotNetAtlas.Test.Framework.Kafka;
using MassTransit;
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
/// Provides MassTransit test harness with EF Core saga persistence and real Kafka consumers.
/// </summary>
public sealed class SagaIntegrationTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqlServerTestContainer _dbContainer;
    private readonly KafkaTestContainer _kafkaContainer = new();

    public const string FinancePaymentsTopic = "finance.payments";
    public const string FinancePaymentCommandsTopic = "finance.payment-commands";
    public const string OrderAlertSubscriptionsTopic = "order.alert-subscriptions";
    public const string WeatherAlertSubscriptionsTopic = "weather.alert-subscriptions";
    public const string WeatherAlertSubscriptionsCommandsTopic = "weather.alert-subscriptions.commands";

    public FakeOutboxWriter FakeOutboxWriter { get; } = new();
    public KafkaTestProducer KafkaProducer { get; private set; } = null!;

    private IBusControl? _busControl;

    public SagaIntegrationTestFixture()
    {
        var migrationsPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(), "saga", "DotNetAtlas.Sagas", "Persistence", "Database",
            "Migrations", "SqlScripts");

        _dbContainer = new SqlServerTestContainer(
            databaseName: "Saga",
            sqlScriptsMigrationsPath: migrationsPath,
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
            .UseKafkaSettings(_kafkaContainer.KafkaOptions)
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
            });
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_dbContainer.StartAsync(), _kafkaContainer.StartAsync());

        var topics = new[]
        {
            OrderAlertSubscriptionsTopic, WeatherAlertSubscriptionsTopic, WeatherAlertSubscriptionsCommandsTopic,
            FinancePaymentsTopic, FinancePaymentCommandsTopic
        };
        await _kafkaContainer.CreateKafkaTopicsAsync(topics);

        KafkaProducer = new KafkaTestProducer(_kafkaContainer.KafkaOptions);

        // Start the MassTransit bus (includes SQL transport for internal saga messages/timeouts)
        _busControl = Services.GetRequiredService<IBusControl>();
        await _busControl.StartAsync();
    }

    public Task ResetDatabaseAsync() => _dbContainer.CleanDataAsync();

    public new async ValueTask DisposeAsync()
    {
        if (_busControl is not null)
        {
            await _busControl.StopAsync();
        }

        KafkaProducer.Dispose();
        await base.DisposeAsync();
        await _dbContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
