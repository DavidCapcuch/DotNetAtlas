using FastEndpoints;

namespace Ordering.API.AlertSubscriptionOrders;

internal sealed class AlertSubscriptionOrdersGroup : Group
{
    public const string GroupName = "alert-subscriptions";

    public AlertSubscriptionOrdersGroup()
    {
        Configure("/alert-subscriptions", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName(GroupName));
            ep.Tags(GroupName);
        });
    }
}
