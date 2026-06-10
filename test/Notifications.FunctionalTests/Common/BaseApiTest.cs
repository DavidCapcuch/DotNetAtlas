using Microsoft.Extensions.DependencyInjection;
using Notifications.FunctionalTests.Common.TestClientInfrastructure;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Notifications.FunctionalTests.Common;

public abstract class BaseApiTest : IAsyncLifetime
{
    private readonly Func<Task> _resetFixtureStateAsync;
    private readonly TestCaseTracer _testCaseTracer;

    protected IServiceScope Scope { get; }
    protected SignalRClientFactory SignalRClientFactory { get; }

    protected BaseApiTest(ApiTestFixture app)
    {
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();

        // In local Jaeger, each test method surfaces as a named trace operation.
        _testCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "functional");

        SignalRClientFactory = new SignalRClientFactory(
            app.Server,
            _testCaseTracer.TraceParent,
            app.TokenCreator,
            TestContext.Current.CancellationToken);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (TestContext.Current.TestState?.Result == TestResult.Failed)
        {
            _testCaseTracer.RecordTestFailure(TestContext.Current.TestState.ExceptionMessages);
        }

        await _resetFixtureStateAsync();
        _testCaseTracer.Dispose();
        Scope.Dispose();
    }
}
