using FastEndpoints;

namespace Ordering.Api.Endpoints.Orders;

/// <summary>
/// FastEndpoints group for the <c>/api/v1/ordering/orders/...</c> route family
/// (per ADR-0012 versioned-route convention). Authentication is the default;
/// individual endpoints opt into <c>AuthPolicies.OrderingAdmin</c> for the
/// admin-only routes.
/// </summary>
internal sealed class OrdersGroup : Group
{
    public OrdersGroup()
    {
        // Group route: "ordering/orders" -> combined with FastEndpoints'
        // Endpoints.RoutePrefix="api" and Versioning.Prefix="v", endpoints
        // resolve to /api/v1/ordering/orders/... (ADR-0012).
        Configure("ordering/orders", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName(EndpointGroupConstants.Orders));
            ep.Tags(EndpointGroupConstants.Orders);
        });
    }
}
