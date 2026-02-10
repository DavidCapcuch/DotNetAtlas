using DotNetAtlas.Sagas.Common.Observability.Tracing;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability;

/// <summary>
/// Activity tags specific to the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/>.
/// For common tags, see <see cref="SagaActivityTags"/>.
/// </summary>
public static class AlertSubscriptionExtensionSagaActivityTags
{
    /// <summary>
    /// The duration in days for the subscription extension.
    /// </summary>
    public const string DurationDays = "saga.duration_days";

    /// <summary>
    /// The new expiration date after extension.
    /// </summary>
    public const string NewExpiresAt = "saga.new_expires_at";
}
