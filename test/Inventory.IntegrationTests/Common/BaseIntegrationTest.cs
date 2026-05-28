using Inventory.Infrastructure.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Base class for Inventory integration tests sharing the
/// <see cref="IntegrationTestFixture"/>. Captures a per-test
/// <see cref="IServiceScope"/> and an <see cref="InventoryDbContext"/> for
/// the common "spin up DI, exercise a handler, verify DB rows" pattern, and
/// wires <see cref="TestCaseTracer"/> so each test method shows up as its own
/// trace in local Jaeger.
/// </summary>
/// <remarks>
/// The <see cref="Fixture"/> property is preserved because many tests use
/// <c>Fixture.CreateScope()</c> and <c>Fixture.ConnectionString</c> directly
/// to drive multi-scope scenarios (concurrency interceptors, raw-SQL seeds).
/// Per-test reset is invoked in <see cref="DisposeAsync"/> via
/// <see cref="IntegrationTestFixture.ResetFixtureStateAsync"/>.
/// </remarks>
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly TestCaseTracer _testCaseTracer;
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IntegrationTestFixture Fixture { get; }
    protected IServiceScope Scope { get; }
    protected InventoryDbContext InventoryDbContext { get; }
    protected StockItemSeed Seed { get; }

    protected BaseIntegrationTest(IntegrationTestFixture app)
    {
        Fixture = app;

        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        InventoryDbContext = Scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Seed = new StockItemSeed(app);

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
