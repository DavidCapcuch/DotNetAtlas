using DotNetAtlas.Test.Framework;
using DotNetAtlas.Test.Framework.Database;
using DotNetAtlas.Test.Framework.Kafka;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Persistence.Database;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace SagaOrchestrators.IntegrationTests.Common;

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

    public FakeOutboxWriter FakeOutboxWriter { get; } = new();
    public KafkaTestProducer KafkaProducer { get; private set; } = null!;

    private IBusControl? _busControl;

    public SagaIntegrationTestFixture()
    {
        var migrationsPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(), "saga", "SagaOrchestrators", "Common", "Persistence", "Database",
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

        // we cannot access Services for IOptions here because that automatically starts the server, and the
        // server will fail to start without topics pre-created
        var topicsOptions = LoadTopicsFromConfiguration();
        await _kafkaContainer.CreateKafkaTopicsAsync(topicsOptions.GetAllTopics());

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

    private static SagaTopicsOptions LoadTopicsFromConfiguration()
    {
        var sagasProjectPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(), "saga", "SagaOrchestrators");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(sagasProjectPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Testing.json", optional: false)
            .Build();

        return configuration.GetSection(SagaTopicsOptions.Section).Get<SagaTopicsOptions>()
               ?? throw new InvalidOperationException(
                   $"Failed to bind configuration section '{SagaTopicsOptions.Section}' to {nameof(SagaTopicsOptions)}. " +
                   "Verify appsettings.json contains the required Kafka topic values.");
    }
}
