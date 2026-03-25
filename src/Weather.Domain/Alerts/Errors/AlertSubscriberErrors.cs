using Platform.SharedKernel.Errors;

namespace Weather.Domain.Alerts.Errors;

public static class AlertSubscriberErrors
{
    public static ValidationError MaxSubscriptionsReached(int max)
        => new ValidationError(
            propertyName: "Subscriptions",
            errorMessage: $"User cannot have more than {max} active subscriptions.",
            errorCode: "Alert.MaxSubscriptionsReached");

    public static NotFoundError SubscriberNotFound(Guid userId)
        => new NotFoundError(nameof(AlertSubscriber), userId, "Subscriber.NotFound");

    public static ValidationError CannotDowngradeActiveSubscription()
        => new ValidationError(
            propertyName: "Subscription",
            errorMessage: "Cannot downgrade while subscription is still active. Please wait until subscription expires.",
            errorCode: "Alert.CannotDowngradeActiveSubscription");
}
