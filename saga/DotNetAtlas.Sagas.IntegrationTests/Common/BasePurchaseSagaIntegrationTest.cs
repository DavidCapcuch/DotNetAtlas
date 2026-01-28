using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga;
using MassTransit.Testing;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

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
