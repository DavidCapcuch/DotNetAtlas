namespace DotNetAtlas.Api.Common.Extensions
{
    public static class HostEnvironmentExtensions
    {
        /// <summary>
        /// Checks if the current host environment name is Local.
        /// </summary>
        /// <param name="hostEnvironment">An instance of <see cref="T:Microsoft.Extensions.Hosting.IHostEnvironment" />.</param>
        /// <returns>True if the environment name is Local, otherwise false.</returns>
        public static bool IsLocal(this IHostEnvironment hostEnvironment)
        {
            return hostEnvironment?.IsEnvironment("Local") ?? throw new ArgumentNullException(nameof(hostEnvironment));
        }

        /// <summary>
        /// Checks if the current host environment name is Test. 
        /// </summary>
        /// <param name="hostEnvironment">An instance of <see cref="T:Microsoft.Extensions.Hosting.IHostEnvironment" />.</param>
        /// <returns>True if the environment name is Test, otherwise false.</returns>
        public static bool IsTest(this IHostEnvironment hostEnvironment)
        {
            return hostEnvironment?.IsEnvironment("Test") ?? throw new ArgumentNullException(nameof(hostEnvironment));
        }
    }
}