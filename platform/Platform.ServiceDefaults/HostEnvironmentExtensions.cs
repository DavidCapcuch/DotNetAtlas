using Microsoft.Extensions.Hosting;

namespace Platform.ServiceDefaults;

/// <summary>
/// Extension methods for <see cref="IHostEnvironment"/> implementing this codebase's three-tier
/// environment taxonomy:
/// <list type="bullet">
/// <item><c>Development</c> (the .NET-vanilla default): the developer's environment — both
/// <c>dotnet run</c> on a laptop AND the <c>docker-compose</c> stack. Use the MS-built-in
/// <see cref="HostEnvironmentEnvExtensions.IsDevelopment(IHostEnvironment)"/> to detect it.</item>
/// <item><c>Testing</c> (custom): test fixtures (<c>WebApplicationFactory&lt;Program&gt;</c>,
/// PostgreSqlTestContainer, etc.). Use <see cref="IsTesting"/>.</item>
/// <item>Anything else (deployed clusters — e.g. <c>Dev</c>, <c>Staging</c>, <c>Production</c>):
/// <see cref="IsDeployedEnvironment"/> returns <c>true</c>.</item>
/// </list>
/// This matches MS-vanilla naming so <c>dotnet run</c> from VS/Rider without explicit overrides
/// hits the laptop tier (no foot-gun where a developer accidentally gets cluster-mode behavior).
/// </summary>
public static class HostEnvironmentExtensions
{
    /// <param name="hostEnvironment">An instance of <see cref="IHostEnvironment" />.</param>
    extension(IHostEnvironment hostEnvironment)
    {
        /// <summary>
        /// Checks if the current host environment name is Testing.
        /// </summary>
        /// <returns>True if the environment name is Testing, otherwise false.</returns>
        public bool IsTesting()
        {
            return hostEnvironment.IsEnvironment("Testing");
        }

        /// <summary>
        /// Checks if the current host environment is a deployed (non-developer, non-test) cluster.
        /// Returns <c>true</c> for any environment name other than <c>Development</c> or <c>Testing</c>
        /// (e.g. <c>Dev</c>, <c>Staging</c>, <c>Production</c>).
        /// </summary>
        /// <returns>True if the environment is a deployed cluster, otherwise false.</returns>
        public bool IsDeployedEnvironment()
        {
            return !(hostEnvironment.IsDevelopment() || hostEnvironment.IsTesting());
        }
    }
}
