using Microsoft.Extensions.Hosting.Internal;

namespace Platform.ServiceDefaults.UnitTests;

/// <summary>
/// Pins the three-tier environment taxonomy in <see cref="HostEnvironmentExtensions"/> that the
/// dev-surface gates (Swagger UI, the developer exception page) and the deployed auth / CORS / HTTPS
/// guards all key on. <see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/> must be true for
/// every environment name other than Development / Testing — the literal-<c>Production</c> fragility
/// this replaces would serve dev surfaces on a tier named Staging or Dev.
/// </summary>
public class HostEnvironmentExtensionsTests
{
    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Staging", true)]
    [InlineData("Production", true)]
    [InlineData("Dev", true)]
    public void IsDeployedEnvironment_IsTrueForEveryNameExceptDevelopmentAndTesting(
        string environmentName,
        bool expectedDeployed)
    {
        var environment = new HostingEnvironment { EnvironmentName = environmentName };

        environment.IsDeployedEnvironment().Should().Be(expectedDeployed);
    }

    [Theory]
    [InlineData("Testing", true)]
    [InlineData("Development", false)]
    [InlineData("Staging", false)]
    public void IsTesting_IsTrueOnlyForTheTestingEnvironment(string environmentName, bool expectedTesting)
    {
        var environment = new HostingEnvironment { EnvironmentName = environmentName };

        environment.IsTesting().Should().Be(expectedTesting);
    }
}
