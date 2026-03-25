using Platform.CQS;

namespace Ordering.Application.AlertSubscriptions.FailAlertSubscriptionOrder;

/// <summary>
/// Internal command to mark an alert subscription order as failed.
/// Dispatched by Kafka consumers when a saga outcome event indicates failure.
/// </summary>
public sealed class FailAlertSubscriptionOrderCommand : ICommand
{
    /// <summary>
    /// The alert subscription order ID to fail.
    /// </summary>
    public required Guid OrderId { get; init; }
}
