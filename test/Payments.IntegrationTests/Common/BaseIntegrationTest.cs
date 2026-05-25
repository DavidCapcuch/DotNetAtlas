using Microsoft.Extensions.DependencyInjection;
using Payments.Infrastructure.Persistence.Database;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Payments.IntegrationTests.Common;

/// <summary>
/// Base for Payments integration tests. Creates a per-test DI scope so each test gets its own
/// <see cref="PaymentsDbContext"/>, wires <see cref="TestCaseTracer"/> for per-test OpenTelemetry
/// activities (Jaeger trace-per-test locally), injects the xUnit test-output sink into the host
/// Serilog pipeline, and resets fixture state on dispose so subsequent tests see a clean slate.
/// </summary>
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly TestCaseTracer _testCaseTracer;
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IServiceScope Scope { get; }
    protected PaymentsDbContext PaymentsDbContext { get; }

    protected BaseIntegrationTest(IntegrationTestFixture app)
    {
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        PaymentsDbContext = Scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

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
