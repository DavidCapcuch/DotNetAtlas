using DotNetAtlas.CQS;
using FluentResults;
using Ordering.Application.Common.Data;

namespace Ordering.Application.AlertSubscriptions.GetAlertSubscriptionOrderStatus;

public sealed class GetAlertSubscriptionOrderStatusQueryHandler
    : IQueryHandler<GetAlertSubscriptionOrderStatusQuery, GetAlertSubscriptionOrderStatusResponse>
{
    private readonly IOrderingDbContext _orderingDbContext;

    public GetAlertSubscriptionOrderStatusQueryHandler(IOrderingDbContext orderingDbContext)
    {
        _orderingDbContext = orderingDbContext;
    }

    public async Task<Result<GetAlertSubscriptionOrderStatusResponse>> HandleAsync(
        GetAlertSubscriptionOrderStatusQuery query,
        CancellationToken ct)
    {
        // Activity.Current?.SetTag(TraceTags.FeedbackId, query.Id.ToString());

        return null;
    }
}
