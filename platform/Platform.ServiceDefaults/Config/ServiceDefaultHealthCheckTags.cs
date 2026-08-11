namespace Platform.ServiceDefaults.Config;

/// <summary>
/// Constants for service defaults including health check endpoint paths and tags.
/// This is the single source of truth for these values across all services.
/// <para>
/// On probe timeouts: the application-lifecycle check registered by
/// <c>AddApplicationLifecycleHealthCheck</c> deliberately carries none, because it only reads the
/// three <see cref="Microsoft.Extensions.Hosting.IHostApplicationLifetime"/> tokens — no I/O — and
/// so hands back an already-completed task, which means a
/// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration.Timeout"/>
/// could never fire on it. That is a property of the pinned package version rather than of the
/// concept, so <c>ApplicationLifecycleHealthCheckTests</c> asserts it instead of trusting it.
/// Anything that performs I/O should carry one. Other registrations
/// currently omit it — <c>AddDbContextCheck</c> exposes no such parameter at all, and a few I/O
/// probes have simply never been given one — but those are gaps, not precedent to copy.
/// </para>
/// </summary>
public static class ServiceDefaultHealthCheckTags
{
    private const string ApiBasePath = "/api";

    /// <summary>
    /// Liveness endpoint — serves whatever carries <see cref="LivenessTag"/>. Most hosts tag
    /// nothing, so it evaluates an empty check set and returns 200 whenever the process answers
    /// HTTP at all. That is the intent, not a misconfiguration: it is a process-reachability probe,
    /// and a green response here is not evidence that any dependency is healthy.
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
    /// Gates <b>traffic</b>: "can this instance serve a request right now?" Takes every dependency
    /// on a request path — database, caches, broker — plus the application-lifecycle check, which
    /// fails until the host has finished starting and again once it is stopping, so a stopping
    /// instance is drained rather than restarted. The not-yet-started half guards a gap this
    /// solution does not currently have — every host starts its Kafka bus before <c>RunAsync</c>,
    /// so the socket opens only once the consumers are up — and becomes load-bearing the moment a
    /// hosted service that must finish before traffic is added. A dependency the service can still
    /// serve traffic without is deliberately excluded: Basket omits Kafka because it publishes
    /// through the outbox, and every unit omits the Schema Registry because it is contacted
    /// cold-cache only. Failing is cheap and self-healing — the instance leaves rotation until it
    /// recovers.
    /// </summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Gates <b>restarts</b>: "is this process wedged in a way only a restart fixes?" Failing here
    /// is destructive, so the bar is deliberately high and most hosts tag nothing at all.
    /// <para>
    /// <b>Never tag a dependency or lifecycle check with this.</b> Dependency state is shared, so a
    /// brief database blip would fail liveness on every replica at once, turning a recoverable
    /// outage into a cluster-wide restart loop. The lifecycle check is worse: it reports unhealthy
    /// while the process is still starting and again once it is stopping — a restart is futile at
    /// the second, and at the first it would kill every slow-starting instance before it ever
    /// served a request. Both belong on <see cref="ReadinessTag"/>.
    /// </para>
    /// </summary>
    public const string LivenessTag = "live";
}

