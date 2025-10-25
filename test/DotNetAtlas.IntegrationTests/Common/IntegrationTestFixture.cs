using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Communication.Kafka.Config;
using DotNetAtlas.Infrastructure.HttpClients.Weather.OpenMeteo;
using DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiCom;
using DotNetAtlas.Infrastructure.SignalR;
using DotNetAtlas.Test.Shared;
using DotNetAtlas.Test.Shared.Database;
using DotNetAtlas.Test.Shared.Kafka;
using DotNetAtlas.Test.Shared.Redis;
using FastEndpoints.Testing;
using Hangfire;
using Hangfire.Storage;
using HealthChecks.UI.Core.HostedService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace DotNetAtlas.IntegrationTests.Common;

internal sealed class ForecastTestCollection : TestCollection<IntegrationTestFixture>;

internal sealed class SignalRTestCollection : TestCollection<IntegrationTestFixture>;

[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly SqlServerTestContainer _dbContainer = new(
        databaseName: "Weather",
        schemas: ["weather", "HangFire"],
        flywayMigrationsPath: SolutionPaths.FlywayMigrationsDirectory,
        withReseed: true);

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    public KafkaTestConsumerRegistry KafkaConsumerRegistry { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _redisContainer.StartAsync(),
            _kafkaContainer.StartAsync());
    }

    protected override ValueTask SetupAsync()
    {
        KafkaConsumerRegistry = new KafkaTestConsumerRegistry(
            Services.GetRequiredService<IOptions<KafkaOptions>>().Value,
            Services.GetRequiredService<IOptions<TopicsOptions>>().Value);
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder builder)
    {
        var kafkaOptions = _kafkaContainer.KafkaOptions;
        builder.ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseSetting($"ConnectionStrings:{ConnectionStrings.Weather}", _dbContainer.ConnectionString);
            webBuilder.UseSetting($"ConnectionStrings:{ConnectionStrings.Redis}",
                _redisContainer.ConfigurationOptions.ToString());

            for (var i = 0; i < kafkaOptions.Brokers.Length; i++)
            {
                webBuilder.UseSetting($"{KafkaOptions.Section}:Brokers:{i}", kafkaOptions.Brokers[i]);
            }

            webBuilder.UseSetting($"{SchemaRegistryOptions.Section}:Url", kafkaOptions.SchemaRegistry.Url);
            webBuilder.UseSetting($"{AvroSerializerOptions.Section}:AutoRegisterSchemas",
                kafkaOptions.AvroSerializer.AutoRegisterSchemas.ToString());
            webBuilder.UseSetting($"{AvroSerializerOptions.Section}:SubjectNameStrategy",
                kafkaOptions.AvroSerializer.SubjectNameStrategy.ToString());
        });

        return base.ConfigureAppHost(builder);
    }

    protected override void ConfigureApp(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment("Testing")
            .ConfigureServices((context, services) =>
            {
                var injectableTestOutputSink = new InjectableTestOutputSink();
                services.AddSingleton<IInjectableTestOutputSink>(injectableTestOutputSink);
                services.AddSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .MinimumLevel.Debug()
                        .ReadFrom.Configuration(context.Configuration)
                        .WriteTo.InjectableTestOutput(injectableTestOutputSink)
                        .Enrich.FromLogContext();
                }, true, true);
            })
            .ConfigureTestServices(services =>
            {
                services.AddScoped<OpenMeteoWeatherProvider>();
                services.AddScoped<WeatherApiComProvider>();
                services.AddSingleton<RedisSignalRGroupManager>();
                services.AddSingleton(Substitute.For<IHealthCheckReportCollector>());
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        await Task.WhenAll(
            _dbContainer.CleanDataAsync(),
            _redisContainer.CleanDataAsync(),
            CleanHangfireJobsAsync()
        );
    }

    private async Task CleanHangfireJobsAsync()
    {
        // Clear all recurring Hangfire jobs explicitly, SQL cleanup is not enough
        // internal states of hangfire etc. nseed to be cleaned up
        var recurringJobManager = Services.GetRequiredService<IRecurringJobManager>();
        var recurringJobs = Services.GetRequiredService<IBackgroundJobClientV2>()
            .Storage.GetConnection().GetRecurringJobs();

        foreach (var job in recurringJobs)
        {
            recurringJobManager.RemoveIfExists(job.Id);
        }

        await Task.CompletedTask;
    }

    protected override async ValueTask TearDownAsync()
    {
        KafkaConsumerRegistry.Dispose();

        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
