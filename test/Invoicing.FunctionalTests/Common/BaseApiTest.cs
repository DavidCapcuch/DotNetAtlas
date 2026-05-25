using Invoicing.FunctionalTests.Common.TestClientInfrastructure;
using Invoicing.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Invoicing.FunctionalTests.Common;

/// <summary>
/// Base class for Invoicing functional tests. Creates a per-test DI scope so each test
/// gets its own <see cref="InvoicingDbContext"/>, exposes the shared
/// <see cref="HttpClientRegistry{TEntryPoint}"/>, wires the per-test
/// <see cref="TestCaseTracer"/> so each test method appears as its own Jaeger trace in
/// local infrastructure, and resets fixture state (Redis flush + table truncation) on
/// dispose so subsequent tests see a clean slate.
/// </summary>
public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;
    protected TestCaseTracer TestCaseTracer { get; }
    protected IServiceScope Scope { get; }
    protected InvoicingDbContext DbContext { get; }
    protected HttpClientRegistry<Program> HttpClientRegistry { get; }
    protected ApiTestFixture App { get; }

    protected BaseApiTest(ApiTestFixture app)
    {
        App = app;
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<InvoicingDbContext>();

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        TestCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "functional");

        HttpClientRegistry = app.HttpClientRegistry;
        HttpClientRegistry.SetTraceParent(TestCaseTracer.TraceParent);
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
