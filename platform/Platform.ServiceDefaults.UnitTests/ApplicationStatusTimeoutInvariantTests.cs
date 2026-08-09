using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Config;

namespace Platform.ServiceDefaults.UnitTests;

/// <summary>
/// Pins the third-party behaviour that lets all nine units register their application-lifecycle
/// check without a <c>timeout:</c>. The reasoning is recorded on
/// <see cref="ServiceDefaultHealthCheckTags"/>, but it is a property of a pinned package version,
/// not of the concept — so it is asserted here rather than trusted. A package bump that makes the
/// check asynchronous reds this test instead of silently leaving nine readiness probes unbounded.
/// </summary>
public class ApplicationStatusTimeoutInvariantTests
{
    [Fact]
    public async Task ApplicationStatusCheck_CompletesSynchronously_SoNoRegistrationTimeoutIsNeeded()
    {
        using var lifetime = new NeverStoppingLifetime();
        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        services.AddHealthChecks()
            .AddApplicationStatus("Self", tags: [ServiceDefaultHealthCheckTags.ReadinessTag]);

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single();
        var check = registration.Factory(provider);

        var inFlight = check.CheckHealthAsync(
            new HealthCheckContext { Registration = registration },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            inFlight.IsCompleted.Should().BeTrue(
                "the health-check service bounds a check by linking a CTS and calling " +
                "CancelAfter(registration.Timeout) around it — a check that has already completed " +
                "when it returns can never observe that token, which is why none of the nine " +
                "units passes a timeout to AddApplicationStatus");
            (await inFlight).Status.Should().Be(
                HealthStatus.Healthy,
                "a running application reports healthy, so the synchronous completion above is a " +
                "real result rather than a degenerate one");
        }
    }

    /// <remarks>
    /// The tokens must come from real <see cref="CancellationTokenSource"/> instances, not
    /// <see cref="CancellationToken.None"/>: the check decides the application is running by
    /// registering a stopping callback and keeping the returned
    /// <see cref="CancellationTokenRegistration"/>, and <c>Register</c> on a token that can never
    /// be cancelled is a no-op returning a default registration — which the check reads as
    /// "stopped" and reports Unhealthy.
    /// </remarks>
    private sealed class NeverStoppingLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
        }

        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }
}
