using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Observability.Tracing;
using Ordering.Domain.AlertSubscriptionOrders.Errors;
using Ordering.Domain.AlertSubscriptionOrders.Specifications;
using Platform.CQRS;

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
        Activity.Current?.SetTag(TraceTags.AlertSubscriptionOrder, query.Id.ToString());

        var response = await _orderingDbContext.AlertSubscriptionOrders
            .AsNoTracking()
            .WithSpecification(new AlertSubscriptionOrderByIdSpec(query.Id))
            .ProjectToOrderStatusResponse()
            .FirstOrDefaultAsync(ct);

        if (response is null)
        {
            return Result.Fail(AlertSubscriptionOrderErrors.NotFound(query.Id));
        }

        return response;
    }
}
