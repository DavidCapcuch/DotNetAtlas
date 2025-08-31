using System.Diagnostics;
using Bogus;
using DotNetAtlas.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace DotNetAtlas.Application.IntegrationTests.Base;

public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly Func<Task> _resetDatabases;
    protected WeatherForecastContext DbContext { get; }
    protected IServiceScope Scope { get; }
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
        _testActivity = app.Instrumentation.StartActivity(TestContext.Current.TestMethod!.MethodName);
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        var tracerProvider = Scope.ServiceProvider.GetRequiredService<TracerProvider>();
        tracerProvider.ForceFlush();
        _testActivity?.Dispose();
        await _resetDatabases();
        Scope.Dispose();
    }
}
