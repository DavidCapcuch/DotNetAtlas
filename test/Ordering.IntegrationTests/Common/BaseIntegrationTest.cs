using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Persistence.Database;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Ordering.IntegrationTests.Common;

/// <summary>
/// Base class for Ordering integration tests. Injects the per-test Serilog
/// sink so log output is attached to the xUnit test output helper, creates a
/// per-test DI scope, opens a <see cref="TestCaseTracer"/> activity so the
/// test surfaces as a discrete trace in the local Jaeger UI, and resets
/// fixture state (Postgres truncate via Respawn) on dispose so subsequent
/// tests see a clean slate.
/// </summary>
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly TestCaseTracer _testCaseTracer;
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IServiceScope Scope { get; }
    protected OrderingDbContext OrderingDbContext { get; }

    protected BaseIntegrationTest(IntegrationTestFixture app)
    {
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        OrderingDbContext = Scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        _testCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "integration");
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

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
