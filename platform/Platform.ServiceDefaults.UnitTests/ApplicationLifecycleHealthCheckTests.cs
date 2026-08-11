using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Config;

namespace Platform.ServiceDefaults.UnitTests;

/// <summary>
/// Pins what every unit relies on when it registers the application-lifecycle check on readiness:
/// it fails while the host is starting and again once it is stopping, it registers under the name
/// the per-unit tests hard-code, and it is cheap enough to need no registration timeout. Each is a
/// property of a pinned package version rather than of the concept, so each is asserted here rather
/// than trusted — a bump that changes any of them reds this suite instead of silently renaming,
/// unbounding, or un-gating every unit's readiness probe. Why the check belongs on readiness at all
/// lives on <see cref="ServiceDefaultHealthCheckTags.ReadinessTag"/>.
/// </summary>
public class ApplicationLifecycleHealthCheckTests
{
    /// <summary>The package hard-codes this; the type is internal, so no overload can override it.</summary>
    private const string RegisteredName = "ApplicationLifecycle";

    /// <summary>Bounds the startup gate so a faulted host reports its own exception, not a hang.</summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    /// <remarks>
    /// The gap this closes: the third-party check it replaced reported Healthy from construction, so
    /// readiness could never represent "not started at all". <c>ApplicationStarted</c> fires only
    /// once every <see cref="IHostedService"/> has finished starting, which is what this pins.
    /// </remarks>
    [Fact]
    public async Task Readiness_WhileAHostedServiceIsStillStarting_Fails()
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag]);
        builder.Services.AddHostedService(_ => new GatedHostedService(entered, release.Task));

        await using var app = builder.Build();
        app.MapPlatformHealthCheckEndpoints();

        var starting = app.StartAsync(TestContext.Current.CancellationToken);

        HttpStatusCode duringStartup;
        try
        {
            // Resuming only once the gate is entered makes the window deterministic: the server is
            // provably serving, and ApplicationStarted has provably not fired.
            await entered.Task.WaitAsync(GateTimeout, TestContext.Current.CancellationToken);

            using var response = await app.GetTestClient().GetAsync(
                ServiceDefaultHealthCheckTags.ReadinessEndpointPath,
                TestContext.Current.CancellationToken);
            duringStartup = response.StatusCode;
        }
        finally
        {
            // Unconditional: a failure above must surface as its own exception rather than
            // deadlocking disposal against a host still parked mid-start.
            release.TrySetResult();
        }

        await starting;

        using var afterStartup = await app.GetTestClient().GetAsync(
            ServiceDefaultHealthCheckTags.ReadinessEndpointPath,
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            duringStartup.Should().Be(
                HttpStatusCode.ServiceUnavailable,
                "an instance whose hosted services have not started cannot serve traffic, so it " +
                "must be kept out of rotation until they have");
            afterStartup.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "the gate must open once startup completes — a probe that never recovers would " +
                "keep every replica out of rotation forever");
        }
    }

    [Fact]
    public void ApplicationLifecycleCheck_WhenRegistered_UsesTheNameEveryUnitHardCodes()
    {
        using var lifetime = new FakeLifetime(started: true);
        using var registered = Register(lifetime);

        registered.Registration.Name.Should().Be(
            RegisteredName,
            "each unit's readiness set is asserted by name in its own registration test, and the " +
            "check type is internal — so a package rename would red nine suites at once with no " +
            "single place saying why");
    }

    /// <remarks>
    /// The drain half of the contract, and the reason the check sits on readiness rather than
    /// liveness. Without this, a regression that reported Healthy forever after start would pass
    /// every other test here while silently costing every service its graceful drain.
    /// </remarks>
    [Fact]
    public async Task ApplicationLifecycleCheck_WhenApplicationIsStopping_ReportsUnhealthy()
    {
        using var lifetime = new FakeLifetime(started: true);
        using var registered = Register(lifetime);

        lifetime.StopApplication();

        var result = await registered.Check.CheckHealthAsync(
            new HealthCheckContext { Registration = registered.Registration },
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(
            HealthStatus.Unhealthy,
            "a stopping instance must leave rotation before it stops accepting connections, or " +
            "in-flight requests are routed to a process that is already shutting down");
    }

    [Fact]
    public async Task ApplicationLifecycleCheck_CompletesSynchronously_SoNoRegistrationTimeoutIsNeeded()
    {
        using var lifetime = new FakeLifetime(started: true);
        using var registered = Register(lifetime);

        var inFlight = registered.Check.CheckHealthAsync(
            new HealthCheckContext { Registration = registered.Registration },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            inFlight.IsCompleted.Should().BeTrue(
                "the health-check service bounds a check by linking a CTS and calling " +
                "CancelAfter(registration.Timeout) around it — a check that has already completed " +
                "when it returns can never observe that token, which is why no unit passes a " +
                "timeout to AddApplicationLifecycleHealthCheck");
            (await inFlight).Status.Should().Be(
                HealthStatus.Healthy,
                "a started application reports healthy, so the synchronous completion above is a " +
                "real result rather than a degenerate one");
        }
    }

    private static RegisteredCheck Register(IHostApplicationLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddSingleton(lifetime);
        services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck([ServiceDefaultHealthCheckTags.ReadinessTag]);

        var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single();

        return new RegisteredCheck(provider, registration, registration.Factory(provider));
    }

    private sealed record RegisteredCheck(
        ServiceProvider Provider,
        HealthCheckRegistration Registration,
        IHealthCheck Check) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    /// <summary>
    /// Blocks the host mid-start until released, standing in for a hosted service that must finish
    /// before the instance can serve traffic.
    /// </summary>
    /// <remarks>
    /// Gating in <see cref="StartedAsync"/> rather than <see cref="StartAsync"/> is what makes this
    /// independent of registration order. The host runs every <c>StartAsync</c> — including the one
    /// that starts the web server — then every <c>StartedAsync</c>, and only then notifies
    /// <c>ApplicationStarted</c>. Blocking in that third phase therefore lands in the one window
    /// where the server is provably serving and the lifetime is provably not yet started; gating in
    /// <c>StartAsync</c> would race the web server's own registration and usually win.
    /// </remarks>
    private sealed class GatedHostedService(TaskCompletionSource entered, Task release)
        : IHostedLifecycleService
    {
        public async Task StartedAsync(CancellationToken cancellationToken)
        {
            entered.SetResult();
            await release.WaitAsync(cancellationToken);
        }

        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <remarks>
    /// The tokens must come from real <see cref="CancellationTokenSource"/> instances rather than
    /// <see cref="CancellationToken.None"/>: the check reads them to decide which phase the host is
    /// in, so a token that can never be cancelled reads as "Not Started" and reports Unhealthy —
    /// which would let the Healthy assertions above pass for the wrong reason.
    /// </remarks>
    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public FakeLifetime(bool started)
        {
            if (started)
            {
                _started.Cancel();
            }
        }

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }
}
