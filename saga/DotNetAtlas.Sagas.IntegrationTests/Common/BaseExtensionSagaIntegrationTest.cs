using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga;
using MassTransit.Testing;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

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
