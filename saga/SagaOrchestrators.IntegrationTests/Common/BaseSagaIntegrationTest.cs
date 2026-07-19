using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Tracing;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Persistence.Database;
using SagaOrchestrators.Common.SagaAbstractions;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace SagaOrchestrators.IntegrationTests.Common;

/// <summary>
/// Base class for saga integration tests with proper test isolation and cleanup.
/// </summary>
public abstract class BaseSagaIntegrationTest : IAsyncLifetime
{
    private readonly SagaIntegrationTestFixture _fixture;
    private readonly TestCaseTracer _testCaseTracer;

    protected SagaTopicsOptions TopicsOptions { get; }
    protected IServiceScope Scope { get; }
    protected SagaDbContext SagaDbContext { get; }
    protected TimeProvider TimeProvider { get; }
    protected KafkaTestProducer KafkaTestProducer { get; }

    /// <summary>
    /// The MassTransit bus for publishing internal saga events (e.g., timeout events).
    /// </summary>
    protected IBus Bus => Scope.ServiceProvider.GetRequiredService<IBus>();

    protected BaseSagaIntegrationTest(SagaIntegrationTestFixture fixture)
    {
        _fixture = fixture;

        // Inject test output for logging
        var outputSink = fixture.Services.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        Scope = fixture.Services.CreateScope();
        SagaDbContext = Scope.ServiceProvider.GetRequiredService<SagaDbContext>();
        TimeProvider = Scope.ServiceProvider.GetRequiredService<TimeProvider>();
        TopicsOptions = Scope.ServiceProvider.GetRequiredService<IOptions<SagaTopicsOptions>>().Value;
        KafkaTestProducer = fixture.KafkaProducer;

        // In local Jaeger, you will see a trace operation with the name of each test method that you can examine.
        // Inspired by https://github.com/martinjt/unittest-with-otel/tree/main
        _testCaseTracer = new TestCaseTracer(
            Scope.ServiceProvider,
            TestContext.Current.TestMethod!.MethodName,
            TestContext.Current.TestCase!.UniqueID,
            testType: "saga-integration");
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
        await _fixture.ResetDatabaseAsync();
        Scope.Dispose();
    }

    /// <summary>
    /// Creates a typed saga test helper for fluent state waiting.
    /// </summary>
    protected SagaStateMonitor<TSaga, TSagaState> CreateSagaStateMonitor<TSaga, TSagaState>()
        where TSaga : MassTransitStateMachine<TSagaState>
        where TSagaState : class, ISagaStateInstance
    {
        var stateMachine = Scope.ServiceProvider.GetRequiredService<TSaga>();
        return new SagaStateMonitor<TSaga, TSagaState>(SagaDbContext, stateMachine);
    }
}
