using DotNetAtlas.CQS;
using FastEndpoints;

namespace Ordering.Application.AlertSubscriptions.GetAlertSubscriptionOrderStatus;

public class GetAlertSubscriptionOrderStatusQuery : IQuery<GetAlertSubscriptionOrderStatusResponse>
{
    /// <summary>
    /// ID of the requested subscription order.
    /// </summary>
    [RouteParam]
    public required Guid Id { get; set; }
}
