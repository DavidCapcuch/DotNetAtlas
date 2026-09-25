using Microsoft.Extensions.DependencyInjection;
using Payments.Infrastructure.Persistence.Database;
using Payments.IntegrationTests.Common.TestClientInfrastructure;
using Platform.Test.Framework.Tracing;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace Payments.IntegrationTests.Common;

/// <summary>
/// Base for every Payments integration test. Exposes the HTTP edge (<see cref="HttpClientRegistry"/>)
/// for slice tests that enter through an endpoint, the <see cref="Fixture"/> for tests that drive a
/// Kafka handler, and a per-test DI scope with its <see cref="PaymentsDbContext"/> for arranging
/// state. Resets fixture state on dispose.
/// </summary>
public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly TestCaseTracer _testCaseTracer;

    protected IntegrationTestFixture Fixture { get; }

    protected IServiceScope Scope { get; }

    protected PaymentsDbContext DbContext { get; }

    protected HttpClientRegistry<Program> HttpClientRegistry { get; }

    protected BaseIntegrationTest(IntegrationTestFixture app)
    {
        Fixture = app;
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        Scope = app.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        HttpClientRegistry = app.HttpClientRegistry;

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        _testCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "integration");
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
        await Fixture.ResetFixtureStateAsync();
        Scope.Dispose();
    }
}
