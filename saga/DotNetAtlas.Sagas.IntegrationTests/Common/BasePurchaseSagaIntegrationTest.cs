using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using MassTransit.Testing;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

/// <summary>
/// Base class for Purchase saga integration tests.
/// </summary>
public abstract class BasePurchaseSagaIntegrationTest : BaseSagaIntegrationTest
{
    protected ISagaStateMachineTestHarness<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState> SagaHarness { get; }

    protected BasePurchaseSagaIntegrationTest(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaHarness = TestHarness.GetSagaStateMachineHarness<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>();
    }
}
