using Microsoft.Extensions.DependencyInjection;
using Payments.FunctionalTests.Common.TestClientInfrastructure;
using Payments.Infrastructure.Persistence.Database;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Payments.FunctionalTests.Common;

/// <summary>
/// Base class for Payments functional tests. Creates a per-test DI scope so
/// each test gets its own <see cref="PaymentsDbContext"/>, exposes the shared
/// <see cref="HttpClientRegistry{TEntryPoint}"/>, wires <see cref="TestCaseTracer"/>
/// for per-test OpenTelemetry activities (Jaeger trace-per-test locally), injects
/// the xUnit test-output sink into the host Serilog pipeline, and resets fixture
/// state (table truncation) on dispose so subsequent tests see a clean slate.
/// </summary>
public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;
    protected TestCaseTracer TestCaseTracer { get; }
    protected IServiceScope Scope { get; }
    protected PaymentsDbContext DbContext { get; }
    protected HttpClientRegistry<Program> HttpClientRegistry { get; }
    protected ApiTestFixture App { get; }

    protected BaseApiTest(ApiTestFixture app)
    {
        App = app;
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        TestCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "functional");

        HttpClientRegistry = app.HttpClientRegistry;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

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
