namespace DotNetAtlas.Api.Common.Extensions;

internal static class HostEnvironmentExtensions
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
}
