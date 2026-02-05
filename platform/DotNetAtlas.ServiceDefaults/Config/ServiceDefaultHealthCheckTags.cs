namespace DotNetAtlas.ServiceDefaults.Config;

/// <summary>
/// Constants for service defaults including health check endpoint paths and tags.
/// This is the single source of truth for these values across all DotNetAtlas services.
/// </summary>
public static class ServiceDefaultHealthCheckTags
{
    private const string ApiBasePath = "/api";

    /// <summary>
    /// Path for liveness health check endpoint.
    /// </summary>
    public const string HealthEndpointPath = $"{ApiBasePath}/healthz";

    /// <summary>
    /// Path for readiness health check endpoint.
    /// </summary>
    public const string ReadinessEndpointPath = $"{ApiBasePath}/readiness";

    /// <summary>
    /// Path for Prometheus health metrics endpoint.
    /// </summary>
    public const string PrometheusEndpointPath = $"{ApiBasePath}/health/prometheus";

    /// <summary>
    /// Tag for readiness checks
    /// </summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Tag for liveness checks (basic health).
    /// </summary>
    public const string LivenessTag = "live";
}

