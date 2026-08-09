using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;

namespace Platform.ServiceDefaults.UnitTests;

/// <summary>
/// Pins the two contracts <see cref="WebApplicationExtensions.MapPlatformHealthCheckEndpoints"/>
/// owns and the compiler cannot: liveness and readiness must run disjoint check sets, and a deployed
/// tier must emit the aggregate status alone on these unauthenticated endpoints. The why for both
/// lives on <see cref="ServiceDefaultHealthCheckTags.LivenessTag"/> and in that method's remarks.
/// Scope: the helper only — which checks a service registers under which tag is pinned by that
/// unit's own registration tests.
/// </summary>
public class MapPlatformHealthCheckEndpointsTests
{
    private const string LivenessCheckName = "synthetic-liveness";
    private const string ReadinessCheckName = "synthetic-readiness";
    private const string LeakedExceptionMessage = "kaboom";

    [Fact]
    [Trait("Category", "security")]
    public async Task MapPlatformHealthCheckEndpoints_WhenDeployed_ReportsOnlyAggregateStatus()
    {
        await using var app = await BuildHealthCheckHostAsync("Staging");
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            ServiceDefaultHealthCheckTags.ReadinessEndpointPath,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Should().Be(
                """{"status":"Healthy"}""",
                "a deployed tier emits the aggregate status alone on an unauthenticated endpoint");
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task MapPlatformHealthCheckEndpoints_WhenDeployedAndCheckThrew_LeaksNeitherNameNorException()
    {
        await using var app = await BuildHealthCheckHostAsync(
            "Staging",
            HealthStatus.Unhealthy,
            new InvalidOperationException(LeakedExceptionMessage));
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            ServiceDefaultHealthCheckTags.ReadinessEndpointPath,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                "redacting the body must not cost the orchestrator its 503 gate");
            body.Should().Be(
                """{"status":"Unhealthy"}""",
                "the failure path is the only one where there is anything to leak, so it is the " +
                "one that has to be redacted");
            body.Should().NotContain(
                LeakedExceptionMessage,
                "raw exception text from a dependency must never reach an unauthenticated endpoint");
            body.Should().NotContain(
                ReadinessCheckName,
                "per-check names disclose internal dependency topology");
        }
    }

    [Fact]
    public async Task MapPlatformHealthCheckEndpoints_WhenDeveloperTier_ReportsPerCheckDetail()
    {
        await using var app = await BuildHealthCheckHostAsync("Testing");
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            ServiceDefaultHealthCheckTags.ReadinessEndpointPath,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body.Should().Contain(
                ReadinessCheckName,
                "developer tiers surface per-check detail, which is why the endpoint is useful locally");
            body.Should().NotContain(
                LivenessCheckName,
                "readiness must report only its own tagged checks, never the liveness-only set");
        }
    }

    [Fact]
    public async Task MapPlatformHealthCheckEndpoints_WhenReadinessCheckUnhealthy_FailsReadinessOnly()
    {
        await using var app = await BuildHealthCheckHostAsync("Testing", HealthStatus.Unhealthy);
        var client = app.GetTestClient();

        using var readiness = await client.GetAsync(
            ServiceDefaultHealthCheckTags.ReadinessEndpointPath,
            TestContext.Current.CancellationToken);
        using var liveness = await client.GetAsync(
            ServiceDefaultHealthCheckTags.HealthEndpointPath,
            TestContext.Current.CancellationToken);
        var livenessBody = await liveness.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            readiness.StatusCode.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                "an unhealthy dependency must stop traffic being routed to this instance");
            liveness.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "a dependency outage must not fail liveness — that restarts every replica at once");
            livenessBody.Should().NotContain(
                ReadinessCheckName,
                "the liveness endpoint must not execute readiness-tagged checks at all");
            livenessBody.Should().Contain(
                LivenessCheckName,
                "liveness still runs its own tagged checks, so the empty-set case is not what passed here");
        }
    }

    [Fact]
    public async Task MapPlatformHealthCheckEndpoints_WhenNothingIsTaggedForLiveness_StillReportsHealthy()
    {
        await using var app = await BuildReadinessOnlyHostAsync();
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            ServiceDefaultHealthCheckTags.HealthEndpointPath,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "this is the deployed shape of every host but the OutboxRelay — an empty check set " +
            "aggregates to Healthy, which is what makes /api/healthz a process-reachability probe " +
            "rather than a misconfiguration");
    }

    [Fact]
    public async Task MapPlatformHealthCheckEndpoints_WhenReadinessCheckDegraded_KeepsServingTraffic()
    {
        await using var app = await BuildHealthCheckHostAsync("Testing", HealthStatus.Degraded);
        var client = app.GetTestClient();

        using var response = await client.GetAsync(
            ServiceDefaultHealthCheckTags.ReadinessEndpointPath,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "Degraded maps to 200 by default, and services rely on it to report a partial " +
            "failure without being pulled out of rotation");
    }

    private static async Task<WebApplication> BuildHealthCheckHostAsync(
        string environmentName,
        HealthStatus readinessStatus = HealthStatus.Healthy,
        Exception? readinessException = null)
    {
        var builder = CreateTestServerBuilder(environmentName);

        builder.Services.AddHealthChecks()
            .AddCheck(
                LivenessCheckName,
                () => HealthCheckResult.Healthy(),
                tags: [ServiceDefaultHealthCheckTags.LivenessTag])
            .AddCheck(
                ReadinessCheckName,
                () => new HealthCheckResult(readinessStatus, exception: readinessException),
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag]);

        return await StartAsync(builder);
    }

    private static async Task<WebApplication> BuildReadinessOnlyHostAsync()
    {
        var builder = CreateTestServerBuilder("Testing");

        builder.Services.AddHealthChecks()
            .AddCheck(
                ReadinessCheckName,
                () => HealthCheckResult.Healthy(),
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag]);

        return await StartAsync(builder);
    }

    private static WebApplicationBuilder CreateTestServerBuilder(string environmentName)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
        });
        builder.WebHost.UseTestServer();

        return builder;
    }

    private static async Task<WebApplication> StartAsync(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        app.MapPlatformHealthCheckEndpoints();

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
