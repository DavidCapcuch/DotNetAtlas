namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability;

/// <summary>
/// Activity tags specific to the Subscription Purchase saga.
/// For common tags, see <see cref="Common.Observability.SagaActivityTags"/>.
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
