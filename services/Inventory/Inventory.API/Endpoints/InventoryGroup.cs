using FastEndpoints;

namespace Inventory.API.Endpoints;

/// <summary>
/// FastEndpoints group for the Inventory admin HTTP surface. Combined with
/// the platform-level <c>config.Endpoints.RoutePrefix = "api"</c> and
/// <c>config.Versioning.Prefix = "v"</c> + <c>DefaultVersion = 1</c> set in
/// <c>FastEndpointsDependencyInjection.UseInventoryFastEndpoints</c>, this
/// produces routes under <c>/api/v1/inventory/...</c> per ADR-0012.
/// Endpoints inside the group choose their own sub-prefix
/// (<c>stock-items/...</c>, <c>reservations/...</c>).
/// </summary>
internal sealed class InventoryGroup : Group
{
    public InventoryGroup()
    {
        Configure("/inventory", ep =>
        {
            ep.Description(builder => builder.WithGroupName("Inventory"));
            ep.Tags("Inventory");
        });
    }
}
