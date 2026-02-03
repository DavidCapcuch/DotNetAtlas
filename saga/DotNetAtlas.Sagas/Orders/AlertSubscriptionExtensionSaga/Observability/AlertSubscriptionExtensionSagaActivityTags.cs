namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability;

/// <summary>
/// Activity tags specific to the Subscription Extension saga.
/// For common tags, see <see cref="Common.Observability.SagaActivityTags"/>.
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
