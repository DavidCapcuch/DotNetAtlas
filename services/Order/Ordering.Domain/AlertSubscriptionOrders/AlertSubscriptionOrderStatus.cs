namespace Ordering.Domain.AlertSubscriptionOrders;

/// <summary>
/// Status of a subscription order through its lifecycle.
/// </summary>
public enum AlertSubscriptionOrderStatus
{
    Initiated = 0,
    Completed = 1,
    Failed = 2
}
