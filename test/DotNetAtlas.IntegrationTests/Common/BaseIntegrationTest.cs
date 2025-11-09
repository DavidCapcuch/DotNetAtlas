using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Test.Framework.Kafka;
using DotNetAtlas.Test.Framework.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace DotNetAtlas.IntegrationTests.Common;

public abstract class BaseIntegrationTest : IAsyncLifetime
{
    private readonly TestCaseTracer _testCaseTracer;
    private readonly Func<Task> _resetFixtureStateAsync;

    protected IServiceScope Scope { get; }
    protected WeatherDbContext WeatherDbContext { get; }
    protected KafkaTestConsumerRegistry KafkaTestConsumerRegistry { get; }

    protected BaseIntegrationTest(IntegrationTestFixture app)
    {
        var outputSink = app.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        _resetFixtureStateAsync = app.ResetFixtureStateAsync;
        Scope = app.Services.CreateScope();
        WeatherDbContext = Scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
        KafkaTestConsumerRegistry = app.KafkaConsumerRegistry;

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

        _testCaseTracer.Dispose();
        await _resetFixtureStateAsync();
        Scope.Dispose();
    }
}
