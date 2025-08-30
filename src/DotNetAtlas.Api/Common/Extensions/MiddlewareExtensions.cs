using DotNetAtlas.Api.Common.Middlewares;
using Serilog;

namespace DotNetAtlas.Api.Common.Extensions
{
    public static class MiddlewareExtensions
    {
        /// <summary>
        /// Adds request context enrichment (CorrelationId, UserId) to logs and traces
        /// and configures Serilog request logging.
        /// Enrichment runs before Serilog logging so that properties appear in log events.
        /// </summary>
        /// <remarks>
        /// To enrich with UserId, MUST be called AFTER <code>UseAuthentication()</code>
        /// and to log requests that fail authorization, called BEFORE <code>UseAuthorization()</code>
        /// </remarks>
        public static IApplicationBuilder UseRequestContextTelemetry(this IApplicationBuilder app)
        {
            app.UseMiddleware<RequestContextEnrichmentMiddleware>();
            app.UseSerilogRequestLogging();

            return app;
        }
    }
}