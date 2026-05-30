namespace Catalog.Api.Common.Authorization;

/// <summary>
/// Authorisation policy names for the Catalog bounded context. Constants live in the Api
/// layer so the policy DI registration (<see cref="AuthenticationDependencyInjection"/>) and
/// the FastEndpoints <c>Policies(...)</c> attribute reference the same string by symbol —
/// avoids typo drift between registration and enforcement.
/// </summary>
/// <remarks>
/// Per ADR-0010: the read policy is satisfied by <see cref="Scopes.CatalogRead"/> OR
/// <see cref="Scopes.CatalogWrite"/> (service-to-service reads; write implies read); the write
/// policy requires the <see cref="Roles.Admin"/> realm role AND <see cref="Scopes.CatalogWrite"/>
/// (defense-in-depth for human-admin product / category mutations). Both are enforced inside
/// <see cref="AuthenticationDependencyInjection.AddCatalogAuthentication"/>.
/// </remarks>
internal static class AuthPolicies
{
    /// <summary>Gates Catalog query endpoints — satisfied by <c>catalog.read</c> or <c>catalog.write</c>.</summary>
    public const string ReadPolicy = "CatalogReadScope";

    /// <summary>Gates Catalog admin command endpoints — requires the <c>admin</c> role AND <c>catalog.write</c>.</summary>
    public const string WritePolicy = "CatalogWriteScope";
}
