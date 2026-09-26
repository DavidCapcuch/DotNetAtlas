using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common.TestClientInfrastructure;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Ordering.IntegrationTests.Common;

/// <summary>
/// Base for every Ordering integration test: the HTTP edge (<see cref="HttpClientRegistry"/>), the
/// <see cref="Fixture"/> for fresh per-step scopes, the fake outbox and the signer, and a per-test
/// <see cref="OrderingDbContext"/> for arranging state and asserting persisted outcomes. Each test
/// surfaces as its own trace in the local Jaeger UI, and
/// <see cref="IntegrationTestFixture.ResetFixtureStateAsync"/> runs on dispose.
/// </summary>
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly TestCaseTracer _testCaseTracer;
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IntegrationTestFixture Fixture { get; }

    protected IServiceScope Scope { get; }

    protected OrderingDbContext DbContext { get; }

    protected HttpClientRegistry<Program> HttpClientRegistry { get; }

    protected BaseIntegrationTest(IntegrationTestFixture app)
    {
        Fixture = app;

        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        HttpClientRegistry = app.HttpClientRegistry;

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        _testCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "integration");

        HttpClientRegistry.SetTraceParent(_testCaseTracer.TraceParent);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (TestContext.Current.TestState?.Result == TestResult.Failed)
        {
            _testCaseTracer.RecordTestFailure(
                TestContext.Current.TestState.ExceptionMessages);
        }

        _testCaseTracer.LogTestTraceLocalJaegerLink();

        _testCaseTracer.Dispose();
        await _resetFixtureStateAsync();
        Scope.Dispose();
    }
}
