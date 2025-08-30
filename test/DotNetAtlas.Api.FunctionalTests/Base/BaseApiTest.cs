using System.Diagnostics;
using System.Net.Http.Headers;
using Bogus;
using DotNetAtlas.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace DotNetAtlas.Api.FunctionalTests.Base
{
    public abstract class BaseApiTest : IAsyncLifetime
    {
        private readonly Func<Task> _resetDatabases;
        protected readonly WeatherForecastContext DbContext;
        protected readonly IServiceScope Scope;
        protected readonly HttpClient NonAuthClient;
        protected readonly HttpClient DevClient;
        protected readonly HttpClient PlebClient;
        private readonly Activity? _testActivity;

        protected BaseApiTest(ApiTestFixture app, ITestOutputHelper testOutputHelper)
        {
            var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
            outputSink.Inject(testOutputHelper);

            Randomizer.Seed = new Random(420_69);
            _resetDatabases = app.ResetDatabasesAsync;
            Scope = app.Services.CreateScope();
            DbContext = Scope.ServiceProvider.GetRequiredService<WeatherForecastContext>();

            // In local Jaeger, you will see a trace operation with name of each test method that you can examine.
            // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main but fixed http propagation below
            _testActivity = app.Instrumentation.StartActivity(TestContext.Current.TestMethod!.MethodName);
            NonAuthClient = app.CreateClient(httpClient =>
            {
                httpClient.DefaultRequestHeaders.Add("traceparent", _testActivity?.Id);
            });
            DevClient = app.CreateClient(httpClient =>
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", FakeTokenCreator.GetAdminUserToken());
                httpClient.DefaultRequestHeaders.Add("traceparent", _testActivity?.Id);
            });
            PlebClient = app.CreateClient(httpClient =>
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", FakeTokenCreator.GetNormalUserToken());
                httpClient.DefaultRequestHeaders.Add("traceparent", _testActivity?.Id);
            });
        }

        public ValueTask InitializeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            var tracerProvider = Scope.ServiceProvider.GetRequiredService<TracerProvider>();
            tracerProvider.ForceFlush();
            _testActivity?.Stop();
            await _resetDatabases();
            Scope.Dispose();
        }
    }
}