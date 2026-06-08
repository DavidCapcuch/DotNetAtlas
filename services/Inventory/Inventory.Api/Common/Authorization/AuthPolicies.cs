namespace Inventory.Api.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Inventory bounded context. Constants live in the Api
/// layer so the policy DI registration (<see cref="AuthenticationDependencyInjection"/>) and
/// the FastEndpoints <c>Policies(...)</c> attribute reference the same string by symbol —
/// avoids typo drift between registration and enforcement.
/// </summary>
/// <remarks>
/// Per ADR-0010: the read policy is satisfied by <see cref="Scopes.InventoryRead"/> OR
/// <see cref="Scopes.InventoryWrite"/> (service-to-service reads, e.g. the public
/// stock-availability display path); the admin-read policy adds the <see cref="Roles.Admin"/>
/// realm role on top (ops/audit reads over <c>OrderId</c>-bearing reservation rows); the write
/// policy requires the <see cref="Roles.Admin"/> realm role AND <see cref="Scopes.InventoryWrite"/>
/// (defense-in-depth for human-admin Receive / Adjust). All three are enforced inside
/// <see cref="AuthenticationDependencyInjection.AddInventoryAuthentication"/>.
/// </remarks>
internal static class AuthPolicies
{
    /// <summary>Gates Inventory service-to-service query endpoints — satisfied by <c>inventory.read</c> or <c>inventory.write</c>.</summary>
    public const string ReadPolicy = "InventoryReadScope";

    /// <summary>Gates Inventory ops/audit reads (reservation-audit) — requires the <c>admin</c> role AND a read-capable scope (<c>inventory.read</c> or <c>inventory.write</c>).</summary>
    public const string AdminReadPolicy = "InventoryAdminReadScope";

    /// <summary>Gates Inventory admin command endpoints — requires the <c>admin</c> role AND <c>inventory.write</c>.</summary>
    public const string WritePolicy = "InventoryWriteScope";
}
