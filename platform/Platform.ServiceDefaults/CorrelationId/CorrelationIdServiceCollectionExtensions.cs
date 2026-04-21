using Microsoft.Extensions.DependencyInjection;

namespace Platform.ServiceDefaults.CorrelationId;

/// <summary>
/// DI extensions for the correlation-id HTTP edge (ADR-0008).
/// </summary>
public static class CorrelationIdServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="CorrelationIdDelegatingHandler"/> for outbound HTTP propagation.
    /// Pair with <c>app.UseCorrelationId()</c> for inbound middleware and
    /// <see cref="AddCorrelationIdPropagation"/> on each typed HttpClient that should propagate.
    /// </summary>
    public static IServiceCollection AddCorrelationId(this IServiceCollection services)
    {
        services.AddTransient<CorrelationIdDelegatingHandler>();
        return services;
    }

    /// <summary>
    /// Opts a named / typed <see cref="HttpClient"/> into correlation-id propagation by attaching the
    /// <see cref="CorrelationIdDelegatingHandler"/>. Opt-in rather than a <c>ConfigureHttpClientDefaults</c>
    /// default so health-check clients aren't inadvertently affected.
    /// </summary>
    public static IHttpClientBuilder AddCorrelationIdPropagation(this IHttpClientBuilder builder)
    {
        return builder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
    }
}
