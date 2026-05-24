using FastEndpoints.Testing;
using Hangfire;
using HealthChecks.UI.Core.HostedService;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OpenTelemetry;
using Platform.Test.Framework;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Redis;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using Weather.Application.WeatherForecast.Common;
using Weather.FunctionalTests.Common.TestClientInfrastructure;
using Weather.Infrastructure.Common.Config;
using Weather.Infrastructure.Persistence.Database;

namespace Weather.FunctionalTests.Common;

internal sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Weather",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectory,
        new RespawnerOptions
        {
            SchemasToInclude = [WeatherDbContext.DefaultSchemaName, "hangfire"]
        });

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "weather-tests");

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();
    }

    protected override ValueTask SetupAsync()
    {
        TokenCreator = new FakeTokenCreator(_signer);
        HttpClientRegistry = new HttpClientRegistry<Program>(this, TokenCreator);
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            var redisConfig = _redisContainer.ConfigurationOptions;
            webBuilder
                .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Weather)}", _dbContainer.ConnectionString)
                .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Redis)}", redisConfig.ToString())
                .UseKafkaSettings(_kafkaContainer.KafkaOptions);
        });

        return base.ConfigureAppHost(a);
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        a
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
                // Relax the OIDC scheme's HTTPS-metadata requirement BEFORE the framework's
                // default IPostConfigureOptions<OpenIdConnectOptions> runs. Using Configure
                // (IConfigureNamedOptions) ensures ordering: all IConfigureOptions run before
                // any IPostConfigureOptions, so the default post-configure sees
                // RequireHttpsMetadata=false and skips the HTTPS-authority throw.
                // PostConfigure would fire too late (after the default already threw).
                services.Configure<OpenIdConnectOptions>(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    options => options.RequireHttpsMetadata = false);
                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps
                // every TokenValidationParameters flag at its production default
                // of TRUE. This intentionally does not live in
                // appsettings.Testing.json so an accidental
                // ASPNETCORE_ENVIRONMENT=Testing on a real host cannot accept
                // these test-signed tokens.
                services.ConfigureJwtBearerForTests(_signer);
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
        _signer.Dispose();
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
