using DotNetAtlas.FunctionalTests.Common.Clients;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Test.Framework.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace DotNetAtlas.FunctionalTests.Common;

public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;
    protected TestCaseTracer TestCaseTracer { get; }
    protected IServiceScope Scope { get; }
    protected WeatherDbContext WeatherDbContext { get; }
    protected HttpClientRegistry<Program> HttpClientRegistry { get; }
    protected SignalRClientFactory SignalRClientFactory { get; }

    protected BaseApiTest(ApiTestFixture app)
    {
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        WeatherDbContext = Scope.ServiceProvider.GetRequiredService<WeatherDbContext>();

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        TestCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "functional");

        HttpClientRegistry = app.HttpClientRegistry;
        HttpClientRegistry.SetTraceParent(TestCaseTracer.TraceId);

        SignalRClientFactory = new SignalRClientFactory(
            app.Server,
            TestCaseTracer.TraceId,
            Scope,
            TestContext.Current.CancellationToken);
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (TestContext.Current.TestState?.Result == TestResult.Failed)
        {
            TestCaseTracer.RecordTestFailure(
                TestContext.Current.TestState.ExceptionMessages);
        }

        await _resetFixtureStateAsync();
        TestCaseTracer.Dispose();
        Scope.Dispose();
    }
}
