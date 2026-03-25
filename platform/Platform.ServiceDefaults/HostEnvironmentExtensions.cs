using Microsoft.Extensions.Hosting;

namespace Platform.ServiceDefaults;

/// <summary>
/// Extension methods for IHostEnvironment to detect custom environments.
/// </summary>
public static class HostEnvironmentExtensions
{
    /// <param name="hostEnvironment">An instance of <see cref="IHostEnvironment" />.</param>
    extension(IHostEnvironment hostEnvironment)
    {
        /// <summary>
        /// Checks if the current host environment name is Local.
        /// </summary>
        /// <returns>True if the environment name is Local, otherwise false.</returns>
        public bool IsLocal()
        {
            return hostEnvironment.IsEnvironment("Local");
        }

        /// <summary>
        /// Checks if the current host environment name is Testing.
        /// </summary>
        /// <returns>True if the environment name is Testing, otherwise false.</returns>
        public bool IsTesting()
        {
            return hostEnvironment.IsEnvironment("Testing");
        }

        /// <summary>
        /// Checks if the current host environment name is deployed in any non-local cluster.
        /// </summary>
        /// <returns>True if the environment is in a cluster, otherwise false.</returns>
        public bool IsDeployedEnvironment()
        {
            return !(hostEnvironment.IsLocal() || hostEnvironment.IsTesting());
        }
    }
}
