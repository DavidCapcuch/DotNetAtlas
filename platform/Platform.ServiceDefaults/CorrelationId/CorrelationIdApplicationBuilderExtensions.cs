using Microsoft.AspNetCore.Builder;

namespace Platform.ServiceDefaults.CorrelationId;

/// <summary>
/// ASP.NET pipeline extensions for the correlation-id middleware.
/// </summary>
public static class CorrelationIdApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="CorrelationIdMiddleware"/> to the pipeline. Place early — before routing,
    /// authentication, and any user code that logs inside the request scope.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
