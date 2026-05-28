using System.Diagnostics;
using SagaOrchestrators.Common.Observability;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability;

/// <summary>
/// OpenTelemetry ActivitySource for the Checkout saga. Reuses the shared
/// <see cref="SagaActivitySource"/>'s underlying source so the OTel registration in
/// <see cref="SagaOrchestrators.Common.ObservabilityDependencyInjection"/> picks it up via
/// the existing <c>AddSource("*")</c> + <c>AddSource(SagaActivitySource.ActivitySourceName)</c>
/// without requiring DI changes.
/// </summary>
/// <remarks>
/// docs/bc-design/checkout-saga.md § 11.1 specifies the activity-source name
/// <c>SagaOrchestrators.Checkout</c>. We keep that as the per-activity operation prefix
/// (e.g., <c>CheckoutSaga.StateTransition.{From}.{To}</c>) but route through the shared
/// source so all saga activities flow to the same OTel pipeline.
/// </remarks>
public static class CheckoutSagaActivitySource
{
    /// <summary>
    /// Logical instrumentation name per § 11.1 - used as a prefix on activity operation names
    /// rather than as a separate <see cref="ActivitySource"/> instance (so OTel registration
    /// stays inside the existing wildcard).
    /// </summary>
    public const string Name = "SagaOrchestrators.Checkout";

    /// <summary>
    /// Starts a new activity for a Checkout-saga operation. Reuses
    /// <see cref="SagaActivitySource.ActivitySource"/> so it inherits the version + OTel
    /// registration of the shared source.
    /// </summary>
    /// <param name="operationName">Logical operation - e.g. nameof(OrderCreatedActivity).</param>
    /// <param name="correlationId">The saga correlation id.</param>
    public static Activity? StartActivity(string operationName, Guid correlationId)
    {
        var activity = SagaActivitySource.ActivitySource.StartActivity($"{Name}.{operationName}");
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(SagaActivityTags.Type, "checkout");
        activity.SetTag(SagaActivityTags.CorrelationId, correlationId.ToString());
        return activity;
    }
}
