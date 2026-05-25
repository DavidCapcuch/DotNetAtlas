using Inventory.FunctionalTests.Common.TestClientInfrastructure;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Inventory.FunctionalTests.Common;

/// <summary>
/// Base class for Inventory functional tests sharing the
/// <see cref="ApiTestFixture"/>. Captures a per-test
/// <see cref="IServiceScope"/> and an <see cref="InventoryDbContext"/> for
/// DB-side assertions, exposes the fixture's <see cref="HttpClientRegistry"/>
/// for HTTP interactions, and wires <see cref="TestCaseTracer"/> so each
/// test method shows up as its own trace in local Jaeger.
/// </summary>
/// <remarks>
/// The <see cref="Fixture"/> property is preserved because tests use
/// <c>Fixture.HttpClientRegistry</c>, <c>Fixture.RedisMultiplexer</c>, and
/// <c>Fixture.Services.CreateAsyncScope()</c> directly. The W3C
/// traceparent header is forwarded to every registry-managed HttpClient so
/// HTTP requests correlate to the test's Jaeger trace.
/// </remarks>
public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;

    protected TestCaseTracer TestCaseTracer { get; }
    protected ApiTestFixture Fixture { get; }
    protected IServiceScope Scope { get; }
    protected InventoryDbContext InventoryDbContext { get; }
    protected HttpClientRegistry<Program> HttpClientRegistry { get; }

    protected BaseApiTest(ApiTestFixture app)
    {
        Fixture = app;

        // xUnit v3 makes TestContext.Current ambient inside the test class
        // ctor — relied on for sink injection here. If a future xUnit
        // upgrade tightens that, move the Inject call into InitializeAsync.
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        InventoryDbContext = Scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

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
