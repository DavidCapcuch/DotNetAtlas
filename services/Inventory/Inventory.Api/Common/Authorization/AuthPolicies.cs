namespace Inventory.Api.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Inventory bounded context. Constants live in the Api
/// layer so the policy DI registration (<see cref="AuthenticationDependencyInjection"/>) and
/// the FastEndpoints <c>Policies(...)</c> attribute reference the same string by symbol —
/// avoids typo drift between registration and enforcement.
/// </summary>
/// <remarks>
/// Per ADR-0010: the read policy is satisfied by <see cref="Scopes.InventoryRead"/> OR
/// <see cref="Scopes.InventoryWrite"/> (service-to-service reads); the write policy requires
/// the <see cref="Roles.Admin"/> realm role AND <see cref="Scopes.InventoryWrite"/>
/// (defense-in-depth for human-admin Receive / Adjust). Both are enforced inside
/// <see cref="AuthenticationDependencyInjection.AddInventoryAuthentication"/>.
/// </remarks>
internal static class AuthPolicies
{
    /// <summary>Gates Inventory query endpoints — satisfied by <c>inventory.read</c> or <c>inventory.write</c>.</summary>
    public const string ReadPolicy = "InventoryReadScope";

    /// <summary>Gates Inventory admin command endpoints — requires the <c>admin</c> role AND <c>inventory.write</c>.</summary>
    public const string WritePolicy = "InventoryWriteScope";
}
