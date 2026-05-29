using FastEndpoints;
using Platform.ServiceDefaults.Idempotency;

namespace Ordering.Api.Common;

/// <summary>
/// Wires the presentation layer for Ordering: FastEndpoints + Swagger, ProblemDetails,
/// and the Idempotency-Key output cache (ADR-0013, backed by <c>redis-cache</c>).
/// Authentication + outbound service-auth registration live in
/// <see cref="AuthenticationDependencyInjection"/> and are wired explicitly from Program.cs.
/// Ordering is an admin/internal API — no CORS is wired.
/// </summary>
internal static class ApiDependencyInjection
{
    /// <summary>
    /// Service-name token written to the Redis key prefix for the
    /// idempotency-key store (<c>ordering-service:idem:</c>) so multiple
    /// services sharing <c>redis-cache</c> do not collide. Keep in sync with
    /// the Keycloak <c>aud</c> claim and OTEL service-name token.
    /// </summary>
    internal const string ServiceName = "ordering-service";

    internal static IServiceCollection AddPresentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOrderingFastEndpoints(configuration);

        services.AddProblemDetails();

        // FastEndpoints' .Idempotency() filter is implemented as an
        // IOutputCachePolicy. AddIdempotency wires that policy as a base
        // policy on OutputCacheOptions; AddIdempotencyKeyOutputCache then
        // swaps the in-memory IOutputCacheStore for a Redis-backed one
        // (ADR-0013 line 141; only the Ordering cancel endpoint uses it
        // in v1). Both calls are additive — same AddOutputCache plumbing.
        services.AddIdempotency();
        services.AddIdempotencyKeyOutputCache(configuration, ServiceName);

        services.AddRazorPages();

        return services;
    }
}
