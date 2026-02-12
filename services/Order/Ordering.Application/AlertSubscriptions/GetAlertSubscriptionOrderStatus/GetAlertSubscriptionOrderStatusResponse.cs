namespace Ordering.Application.AlertSubscriptions.GetAlertSubscriptionOrderStatus;

public class GetAlertSubscriptionOrderStatusResponse
{
    /// <summary>
    /// Unique identifier of the subscription order.
    /// </summary>
    public required Guid Id { get; set; }

    /// <summary>
    /// Current status of the order (Initiated, Completed, or Failed).
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// UTC timestamp when the order was created.
    /// </summary>
    public required DateTime CreatedAtUtc { get; set; }
}
