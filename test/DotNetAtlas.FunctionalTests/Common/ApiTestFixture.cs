using DotNetAtlas.Application.WeatherForecast.Common.Abstractions;
using DotNetAtlas.FunctionalTests.Common.Clients;
using DotNetAtlas.Infrastructure.BackgroundJobs;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Test.Shared;
using DotNetAtlas.Test.Shared.Database;
using DotNetAtlas.Test.Shared.Kafka;
using DotNetAtlas.Test.Shared.Redis;
using FastEndpoints.Testing;
using Hangfire;
using HealthChecks.UI.Core.HostedService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OpenTelemetry;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace DotNetAtlas.FunctionalTests.Common;

internal sealed class FeedbackTestCollection : TestCollection<ApiTestFixture>;

internal sealed class ForecastTestCollection : TestCollection<ApiTestFixture>;

internal sealed class SignalRTestCollection : TestCollection<ApiTestFixture>;

[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly SqlServerTestContainer _dbContainer = new(
        databaseName: "Weather",
        flywayMigrationsPath: SolutionPaths.FlywayMigrationsDirectory,
        new RespawnerOptions
        {
            SchemasToInclude = ["weather", "HangFire"]
        });

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _redisContainer.StartAsync(),
            _kafkaContainer.StartAsync());
    }

    protected override ValueTask SetupAsync()
    {
        HttpClientRegistry = new HttpClientRegistry<Program>(this);
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webBuilder =>
        {
            var redisConfig = _redisContainer.ConfigurationOptions;
            webBuilder
                .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Weather)}", _dbContainer.ConnectionString)
                .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Redis)}", redisConfig.ToString())
                .UseKafkaSettings(_kafkaContainer.KafkaOptions);
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
                // Disable background jobs
                services.AddSingleton(Substitute.For<IRecurringJobManager>());
                services.AddSingleton(Substitute.For<IHealthCheckReportCollector>());
                // API tests don't need a real Kafka producer
                services.AddSingleton(Substitute.For<IForecastEventsProducer>());

                services.AddScoped<FakeWeatherAlertBackgroundJob>();
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        await Task.WhenAll(
            _dbContainer.CleanDataAsync(),
            _redisContainer.CleanDataAsync()
        );
    }

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
