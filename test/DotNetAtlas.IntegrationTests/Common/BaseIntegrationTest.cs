using System.Diagnostics;
using Bogus;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace DotNetAtlas.IntegrationTests.Common;

public abstract class BaseIntegrationTest : IAsyncLifetime
{
    protected WeatherForecastContext DbContext { get; }
    protected IServiceScope Scope { get; }
    private readonly Func<Task> _resetDatabases;
    private readonly Activity? _testActivity;

    protected BaseIntegrationTest(IntegrationTestFixture app, ITestOutputHelper testOutputHelper)
    {
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(testOutputHelper);

        Randomizer.Seed = new Random(420_69);
        _resetDatabases = app.ResetDatabasesAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<WeatherForecastContext>();

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        _testActivity = Scope.ServiceProvider.GetRequiredService<IDotNetAtlasInstrumentation>()
            .StartActivity(TestContext.Current.TestMethod!.MethodName);
        _testActivity?.SetTag("test_trace", true);
        _testActivity?.SetTag("test.run_id", TestContext.Current.TestCase?.UniqueID);
        _testActivity?.SetTag("test_type", "integration");
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

        var tracerProvider = Scope.ServiceProvider.GetRequiredService<TracerProvider>();
        tracerProvider.ForceFlush();
        _testActivity?.Dispose();
        await _resetDatabases();
        Scope.Dispose();
    }
}
