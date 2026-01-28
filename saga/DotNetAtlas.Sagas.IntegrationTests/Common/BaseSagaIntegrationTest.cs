using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga;
using DotNetAtlas.Test.Framework.Tracing;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Sinks.XUnit.Injectable.Abstract;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

/// <summary>
/// Base class for saga integration tests with proper test isolation and cleanup.
/// </summary>
public abstract class BaseSagaIntegrationTest : IAsyncLifetime
{
    private readonly SagaIntegrationTestFixture _fixture;
    private readonly TestCaseTracer _testCaseTracer;

    protected IServiceScope Scope { get; }
    protected ITestHarness TestHarness { get; }
    protected SubscriptionSagaDbContext DbContext { get; }
    protected TimeProvider TimeProvider { get; }

    protected BaseSagaIntegrationTest(SagaIntegrationTestFixture fixture)
    {
        _fixture = fixture;

        // Inject test output for logging
        var outputSink = fixture.ServiceProvider.GetRequiredService<IInjectableTestOutputSink>();
        outputSink.Inject(TestContext.Current.TestOutputHelper!);

        Scope = fixture.ServiceProvider.CreateScope();
        TestHarness = fixture.TestHarness;
        DbContext = Scope.ServiceProvider.GetRequiredService<SubscriptionSagaDbContext>();
        TimeProvider = Scope.ServiceProvider.GetRequiredService<TimeProvider>();

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
}

/// <summary>
/// Base class for Purchase saga integration tests.
/// </summary>
public abstract class BasePurchaseSagaIntegrationTest : BaseSagaIntegrationTest
{
    protected ISagaStateMachineTestHarness<SubscriptionPurchaseSaga, SubscriptionPurchaseSagaState> SagaHarness { get; }

    protected BasePurchaseSagaIntegrationTest(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaHarness = TestHarness.GetSagaStateMachineHarness<SubscriptionPurchaseSaga, SubscriptionPurchaseSagaState>();
    }
}

/// <summary>
/// Base class for Extension saga integration tests.
/// </summary>
public abstract class BaseExtensionSagaIntegrationTest : BaseSagaIntegrationTest
{
    protected ISagaStateMachineTestHarness<SubscriptionExtensionSaga, SubscriptionExtensionSagaState> SagaHarness { get; }

    protected BaseExtensionSagaIntegrationTest(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaHarness = TestHarness.GetSagaStateMachineHarness<SubscriptionExtensionSaga, SubscriptionExtensionSagaState>();
    }
}

/// <summary>
/// Base class for Payment saga integration tests.
/// </summary>
public abstract class BasePaymentSagaIntegrationTest : BaseSagaIntegrationTest
{
    protected ISagaStateMachineTestHarness<PaymentProcessingSaga, PaymentSagaState> SagaHarness { get; }

    protected BasePaymentSagaIntegrationTest(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaHarness = TestHarness.GetSagaStateMachineHarness<PaymentProcessingSaga, PaymentSagaState>();
    }
}
