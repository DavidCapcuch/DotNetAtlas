using FastEndpoints;
using Platform.ServiceDefaults.Idempotency;

namespace Invoicing.Api.Common;

/// <summary>
/// Wires the presentation layer for Invoicing: FastEndpoints + Swagger, ProblemDetails,
/// and the Idempotency-Key output cache (ADR-0013, backed by <c>redis-cache</c>).
/// Authentication lives in <see cref="AuthenticationDependencyInjection"/> and is wired
/// explicitly from Program.cs. Invoicing is an admin/internal API — no CORS is wired.
/// </summary>
internal static class ApiDependencyInjection
{
    /// <summary>
    /// Service-name token written to the Redis key prefix for the idempotency-key store
    /// (<c>invoicing-service:idem:</c>) so multiple services sharing <c>redis-cache</c>
    /// do not collide. Keep in sync with the Keycloak <c>aud</c> claim and OTel service-name
    /// token.
    /// </summary>
    internal const string ServiceName = "invoicing-service";

    internal static IServiceCollection AddPresentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddInvoicingFastEndpoints();

        services.AddProblemDetails();

        // FastEndpoints' .Idempotency() filter is implemented as an IOutputCachePolicy.
        // AddIdempotency wires that policy as a base policy on OutputCacheOptions;
        // AddIdempotencyKeyOutputCache then swaps the in-memory IOutputCacheStore for a
        // Redis-backed one (ADR-0013). v1 only the resend endpoint uses .Idempotency();
        // both calls are additive over the same AddOutputCache plumbing.
        services.AddIdempotency();
        services.AddIdempotencyKeyOutputCache(configuration, ServiceName);

        return services;
    }
}
