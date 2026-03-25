using Platform.SharedKernel.Errors;

namespace Ordering.Domain.AlertSubscriptionOrders.Errors;

/// <summary>
/// Domain error factories for <see cref="AlertSubscriptionOrder"/>.
/// </summary>
public static class AlertSubscriptionOrderErrors
{
    public static NotFoundError NotFound(Guid id)
        => new(nameof(AlertSubscriptionOrder), id, "AlertSubscriptionOrder.NotFound");
}
