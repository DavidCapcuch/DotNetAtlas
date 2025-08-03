using System.Reflection;

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

        /// <summary>
        /// Determines if the current execution is part of the build-time OpenAPI document generation process.
        /// This method checks whether the entry assembly's name matches "GetDocument.Insider", which is used
        /// during the build-time document generation in ASP.NET Core when generating OpenAPI documents.
        /// </summary>
        /// <param name="hostEnvironment">An instance of <see cref="T:Microsoft.Extensions.Hosting.IHostEnvironment" />.</param>
        /// <returns>
        /// <c>true</c> if the current execution is part of the OpenAPI document generation process during the build; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// This method is typically used to conditionally execute logic that should only occur during build time.
        /// For more information refer to the official documentation:
        /// <see href="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi">Generate OpenAPI Documents at Build Time</see>.
        /// </remarks>
        public static bool IsOpenApiGenerationBuild(this IHostEnvironment hostEnvironment)
        {
            return Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
        }
    }
}