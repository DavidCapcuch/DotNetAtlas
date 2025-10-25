using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace DotNetAtlas.Infrastructure.Common.Extensions;

public static class HostEnvironmentExtensions
{
    /// <summary>
    /// Checks if the current host environment name is Local.
    /// </summary>
    /// <param name="hostEnvironment">An instance of <see cref="Microsoft.Extensions.Hosting.IHostEnvironment" />.</param>
    /// <returns>True if the environment name is Local, otherwise false.</returns>
    public static bool IsLocal(this IHostEnvironment hostEnvironment)
    {
        return hostEnvironment.IsEnvironment("Local");
    }

    /// <summary>
    /// Checks if the current host environment name is Testing.
    /// </summary>
    /// <param name="hostEnvironment">An instance of <see cref="Microsoft.Extensions.Hosting.IHostEnvironment" />.</param>
    /// <returns>True if the environment name is Testing, otherwise false.</returns>
    public static bool IsTesting(this IHostEnvironment hostEnvironment)
    {
        return hostEnvironment.IsEnvironment("Testing");
    }

    /// <summary>
    /// Checks if the current host environment name is deployed in any non-local cluster.
    /// </summary>
    /// <param name="hostEnvironment">An instance of <see cref="Microsoft.Extensions.Hosting.IHostEnvironment" />.</param>
    /// <returns>True if the environment is in a cluster, otherwise false.</returns>
    public static bool IsInCluster(this IHostEnvironment hostEnvironment)
    {
        return !(hostEnvironment.IsLocal() || hostEnvironment.IsTesting());
    }
}
