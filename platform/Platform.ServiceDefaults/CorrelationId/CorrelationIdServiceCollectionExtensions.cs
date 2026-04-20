using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.ServiceDefaults.CorrelationId;

/// <summary>
/// DI extensions for the correlation-id HTTP edge (ADR-0008).
/// </summary>
public static class CorrelationIdServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CorrelationIdOptions"/>, <see cref="IHttpContextAccessor"/>, and the
    /// <see cref="CorrelationIdDelegatingHandler"/>. Call once per service.
    /// Pair with <c>app.UseCorrelationId()</c> for inbound middleware and
    /// <see cref="AddCorrelationIdPropagation"/> on each typed HttpClient that should propagate.
    /// </summary>
    public static IServiceCollection AddCorrelationId(this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<CorrelationIdOptions>()
            .BindConfiguration(CorrelationIdOptions.Section);
        services.AddHttpContextAccessor();
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
