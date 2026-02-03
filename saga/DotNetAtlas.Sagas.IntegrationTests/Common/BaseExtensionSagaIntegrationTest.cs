using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using MassTransit.Testing;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

/// <summary>
/// Base class for Extension saga integration tests.
/// </summary>
public abstract class BaseExtensionSagaIntegrationTest : BaseSagaIntegrationTest
{
    protected ISagaStateMachineTestHarness<AlertSubscriptionExtensionSaga, AlertSubscriptionExtensionSagaState> SagaHarness { get; }

    protected BaseExtensionSagaIntegrationTest(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaHarness = TestHarness.GetSagaStateMachineHarness<AlertSubscriptionExtensionSaga, AlertSubscriptionExtensionSagaState>();
    }
}
