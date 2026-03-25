using Platform.CQS;

namespace Ordering.Application.AlertSubscriptions.CompleteAlertSubscriptionOrder;

/// <summary>
/// Internal command to mark an alert subscription order as completed.
/// Dispatched by Kafka consumers when a saga outcome event indicates success.
/// </summary>
public sealed class CompleteAlertSubscriptionOrderCommand : ICommand
{
    /// <summary>
    /// The alert subscription order ID to complete.
    /// </summary>
    public required Guid OrderId { get; init; }
}
