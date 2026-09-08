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
/// Anything that performs I/O should carry one, but a registered <c>timeout:</c> only cancels a
/// token: it bounds a check whose client honours cancellation, and nothing else. Where the client
/// does not — StackExchange.Redis has no cancellable ping, and Npgsql stops honouring the token
/// once past the socket connect — the bound has to come from that client's own timeouts, set on
/// the probe's own connection. A registration declaring a number that nothing enforces is the
/// gap, not the absence of one.
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
    /// serve traffic without is deliberately excluded: Basket omits the Kafka broker probe because
    /// it publishes through the outbox, and every unit omits the Schema Registry because it is
    /// contacted cold-cache only.
    /// <para>
    /// <b>Which status a failing check reports is a routing decision, never a default.</b> Ask: with
    /// this dependency down, does the instance still serve a useful set of requests, the rest cleanly
    /// rejected? <b>No</b> — nothing is left, or a path accepts work it cannot complete — reports
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy"/>, which maps
    /// to 503 and takes the instance out of rotation, binding this service's uptime to that
    /// dependency's. <b>Yes</b> reports
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>, which maps
    /// to 200: the dependency is shared, so failing readiness on it drops every replica at once while
    /// none is broken, trading the set they could still serve for none. Each registration states its
    /// own choice and why.
    /// </para>
    /// <para>
    /// Two costs come with Degraded. It is a bet on the failure being <i>correlated</i>, which no
    /// check can observe — a fault that is really per-instance keeps that one instance in rotation,
    /// so the peer contrast has to come from the exported per-check gauge rather than from routing.
    /// And a Degraded reaches an operator only through that gauge —
    /// <c>UsePlatformHealthChecksPrometheusExporter</c> and the health-checks dashboard — never by
    /// remapping it to 503, and no longer as an <c>unhealthy</c> container in <c>docker compose ps</c>.
    /// </para>
    /// <para>
    /// <c>Kafka topics</c> is the one readiness check that does not probe a live dependency. It
    /// answers whether this instance ever verified that the topics it names exist, and once it has,
    /// it never contacts the broker again — so a later broker outage cannot flip a running fleet
    /// through it. Only an instance that has never verified reports Unhealthy, which is why it can
    /// afford to: it genuinely does not know whether it can do its job. It fixes that status at
    /// registration and no call site can override it, so on a host whose broker check is Degraded a
    /// <i>running</i> instance rides out a broker outage while a <i>restarting</i> one still fails
    /// readiness — broker-outage behaviour there is a function of process age.
    /// </para>
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

