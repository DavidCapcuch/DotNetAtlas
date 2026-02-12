using Ordering.Domain.AlertSubscriptionOrders;
using Riok.Mapperly.Abstractions;

namespace Ordering.Application.AlertSubscriptions.GetAlertSubscriptionOrderStatus;

[Mapper]
public static partial class GetAlertSubscriptionOrderStatusMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    public static partial GetAlertSubscriptionOrderStatusResponse ToOrderStatusResponse(
        this AlertSubscriptionOrder source);

    public static partial IQueryable<GetAlertSubscriptionOrderStatusResponse> ProjectToOrderStatusResponse(
        this IQueryable<AlertSubscriptionOrder> source);
}
