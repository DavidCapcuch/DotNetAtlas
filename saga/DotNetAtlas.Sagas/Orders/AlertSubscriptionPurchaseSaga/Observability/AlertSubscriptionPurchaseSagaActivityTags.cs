using DotNetAtlas.Sagas.Common.Observability.Tracing;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability;

/// <summary>
/// Activity tags specific to the <see cref="AlertSubscriptionPurchaseSaga"/>.
/// For common tags, see <see cref="SagaActivityTags"/>.
/// </summary>
public static class AlertSubscriptionPurchaseSagaActivityTags
{
    /// <summary>
    /// The subscription tier being purchased.
    /// </summary>
    public const string SubscriptionTier = "saga.subscription_tier";

    /// <summary>
    /// The duration in days for the subscription.
    /// </summary>
    public const string DurationDays = "saga.duration_days";
}
