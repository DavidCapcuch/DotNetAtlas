using Basket.Api.Common;
using Basket.Api.Common.Config;

namespace Basket.UnitTests.Api.Common;

/// <summary>
/// Direct coverage for the deployed-environment guard inside
/// <see cref="CorsDependencyInjection.AssertDeployedCorsOptions"/>. Exposed as a
/// static helper so the localhost-with-credentials invariant can be exercised without
/// standing up the full DI container.
/// </summary>
public class CorsDependencyInjectionTests
{
    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:7001")]
    [InlineData("HTTPS://LOCALHOST:7001")]
    public void AssertDeployedCorsOptions_LocalhostOriginWithCredentials_Throws(string origin)
    {
        var options = MakeOptions(origins: [origin], allowCredentials: true);

        var act = () => CorsDependencyInjection.AssertDeployedCorsOptions(options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{BasketCorsOptions.Section}*localhost*");
    }

    [Fact]
    public void AssertDeployedCorsOptions_LocalhostOriginWithoutCredentials_DoesNotThrow()
    {
        var options = MakeOptions(
            origins: ["http://localhost:5173"],
            allowCredentials: false);

        var act = () => CorsDependencyInjection.AssertDeployedCorsOptions(options);

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertDeployedCorsOptions_ProductionOriginsWithCredentials_DoesNotThrow()
    {
        var options = MakeOptions(
            origins: ["https://shop.example.com", "https://admin.example.com"],
            allowCredentials: true);

        var act = () => CorsDependencyInjection.AssertDeployedCorsOptions(options);

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertDeployedCorsOptions_MixedLocalhostAndProductionWithCredentials_Throws()
    {
        var options = MakeOptions(
            origins: ["https://shop.example.com", "http://localhost:5173"],
            allowCredentials: true);

        var act = () => CorsDependencyInjection.AssertDeployedCorsOptions(options);

        act.Should().Throw<InvalidOperationException>();
    }

    private static BasketCorsOptions MakeOptions(string[] origins, bool allowCredentials) =>
        new()
        {
            AllowedOrigins = origins,
            AllowedMethods = ["GET"],
            AllowedHeaders = ["*"],
            AllowCredentials = allowCredentials,
        };
}
