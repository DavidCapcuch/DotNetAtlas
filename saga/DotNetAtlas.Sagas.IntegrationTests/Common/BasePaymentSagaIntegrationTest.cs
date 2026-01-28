using DotNetAtlas.Sagas.Finance.PaymentSaga;
using MassTransit.Testing;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

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
