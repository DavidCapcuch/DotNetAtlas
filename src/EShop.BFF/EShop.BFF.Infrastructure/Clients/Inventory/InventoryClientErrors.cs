using Platform.SharedKernel.Errors;

namespace EShop.BFF.Infrastructure.Clients.Inventory;

/// <summary>Typed errors the Inventory client returns.</summary>
internal static class InventoryClientErrors
{
    /// <summary>
    /// Inventory is unreachable / 5xx / timed out, OR returned 404 (stock item not initialized).
    /// All collapse to "unknown availability" (bff.md § 3.1) — never a gating <c>NotFoundError</c>.
    /// </summary>
    public static ServiceUnavailableError Unavailable(string reason) =>
        new("inventory-service", reason, "Bff.Inventory.Unavailable");
}
