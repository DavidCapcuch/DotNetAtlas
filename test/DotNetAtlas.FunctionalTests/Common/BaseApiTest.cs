using System.Diagnostics;
using System.Net.Http.Headers;
using Bogus;
using DotNetAtlas.Api.SignalR.WeatherAlerts;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Infrastructure.Persistence.Database;
using MessagePack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace DotNetAtlas.FunctionalTests.Common;

public abstract class BaseApiTest : IAsyncLifetime
{
    protected WeatherForecastContext DbContext { get; }
    protected IServiceScope Scope { get; }
    protected HttpClient NonAuthClient { get; }
    protected HttpClient DevClient { get; }
    protected HttpClient PlebClient { get; }
    private readonly Activity? _testActivity;
    private readonly ApiTestFixture _app;
    private readonly Func<Task> _resetDatabases;

    protected BaseApiTest(ApiTestFixture app, ITestOutputHelper testOutputHelper)
    {
        _app = app;
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(testOutputHelper);

        Randomizer.Seed = new Random(420_69);
        _resetDatabases = app.ResetDatabasesAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<WeatherForecastContext>();

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main but fixed http propagation below
        _testActivity = Scope.ServiceProvider.GetRequiredService<IDotNetAtlasInstrumentation>()
            .StartActivity(TestContext.Current.TestMethod!.MethodName);
        _testActivity?.SetTag("test_trace", true);
        _testActivity?.SetTag("test.run_id", TestContext.Current.TestCase?.UniqueID);
        _testActivity?.SetTag("test_type", "functional");

        var traceParent = _testActivity?.Id;
        var devToken = FakeTokenCreator.GenerateDevUserToken();
        var normalToken = FakeTokenCreator.GenerateNormalUserToken();
        DevClient = CreateHttpClient(app, devToken, traceParent);
        PlebClient = CreateHttpClient(app, normalToken, traceParent);
        NonAuthClient = CreateHttpClient(app, null, traceParent);
    }

    private HttpClient CreateHttpClient(ApiTestFixture app, string? accessToken, string? traceParent)
    {
        return app.CreateClient(httpClient =>
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            httpClient.DefaultRequestHeaders.Add("traceparent", traceParent ?? "");
        });
    }

    protected enum ClientTypes
    {
        NonAuth,
        Dev,
        Pleb
    }

    protected async Task<WeatherAlertHubClient> CreateSignalRClientAsync(ClientTypes clientType)
    {
        var accessToken = clientType switch
        {
            ClientTypes.NonAuth => null,
            ClientTypes.Dev => FakeTokenCreator.GenerateDevUserToken(),
            ClientTypes.Pleb => FakeTokenCreator.GenerateNormalUserToken(),
            _ => throw new ArgumentOutOfRangeException(nameof(clientType), clientType, null)
        };
        var hubUrl = new Uri("ws://localhost" + WeatherAlertHub.RoutePattern);

        var hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl.ToString(),
                options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(accessToken);
                    options.Headers.Add("traceparent", _testActivity?.Id ?? "");
                    options.Transports = HttpTransportType.WebSockets;
                    options.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                    options.SkipNegotiation = false;
                    options.WebSocketFactory = async (context, cancellationToken) =>
                    {
                        var wsClient = _app.Server.CreateWebSocketClient();
                        wsClient.SubProtocols.Add("messagepack");
                        wsClient.ConfigureRequest = req =>
                        {
                            if (!string.IsNullOrEmpty(accessToken))
                            {
                                req.Headers.Append("Authorization", $"Bearer {accessToken}");
                            }

                            if (!string.IsNullOrEmpty(_testActivity?.Id))
                            {
                                req.Headers.Append("traceparent", _testActivity.Id);
                            }
                        };
                        return await wsClient.ConnectAsync(context.Uri, cancellationToken);
                    };
                })
            .WithAutomaticReconnect()
            .AddMessagePackProtocol(options => options.SerializerOptions = MessagePackSerializerOptions.Standard)
            .Build();
        var weatherAlertHubClient =
            new WeatherAlertHubClient(
                hubConnection,
                Scope.ServiceProvider.GetRequiredService<IDotNetAtlasInstrumentation>(),
                TestContext.Current.CancellationToken);
        await weatherAlertHubClient.StartAsync();

        return weatherAlertHubClient;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (TestContext.Current.TestState?.Result == TestResult.Failed)
        {
            _testActivity?.AddException(
                new Exception(string.Join(';', TestContext.Current.TestState.ExceptionMessages ?? [])));
            _testActivity?.SetStatus(ActivityStatusCode.Error);
            _testActivity?.SetTag("test_result", "failed");
        }

        await _resetDatabases();

        var tracerProvider = Scope.ServiceProvider.GetRequiredService<TracerProvider>();
        tracerProvider.ForceFlush();
        _testActivity?.Dispose();
        Scope.Dispose();
    }
}
