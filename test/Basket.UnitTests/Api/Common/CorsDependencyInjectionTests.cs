using Basket.Api.Common.Config;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Basket.UnitTests.Api.Common;

/// <summary>
/// Coverage for <see cref="BasketCorsOptionsValidator"/> — the <see cref="IValidateOptions{T}"/>
/// guard wired via <c>AddOptionsWithValidateOnStart</c>. Exercises the wildcard invariant
/// (every environment) and the deployed-env localhost-with-credentials invariant without
/// standing up the host.
/// </summary>
public class CorsDependencyInjectionTests
{
    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:7001")]
    [InlineData("HTTPS://LOCALHOST:7001")]
    [Trait("Category", "security")]
    public void Validate_WhenDeployedWithLocalhostOriginAndCredentials_Fails(string origin)
    {
        // Act
        var result = Validate(Deployed, MakeOptions(origins: [origin], allowCredentials: true));

        // Assert
        using (new AssertionScope())
        {
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain(BasketCorsOptions.Section);
            result.FailureMessage.Should().Contain("localhost");
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public void Validate_WhenDeployedWithLocalhostOriginWithoutCredentials_Succeeds()
    {
        // Act
        var result = Validate(Deployed, MakeOptions(origins: ["http://localhost:5173"], allowCredentials: false));

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "security")]
    public void Validate_WhenDeployedWithProductionOriginsAndCredentials_Succeeds()
    {
        // Act
        var result = Validate(
            Deployed,
            MakeOptions(origins: ["https://shop.example.com", "https://admin.example.com"], allowCredentials: true));

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "security")]
    public void Validate_WhenDeployedWithMixedLocalhostAndProductionAndCredentials_Fails()
    {
        // Act
        var result = Validate(
            Deployed,
            MakeOptions(origins: ["https://shop.example.com", "http://localhost:5173"], allowCredentials: true));

        // Assert
        result.Failed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "security")]
    public void Validate_WhenDevelopmentWithLocalhostOriginAndCredentials_Succeeds()
    {
        // The localhost guard only fires once deployed — dev keeps localhost + credentials.

        // Act
        var result = Validate(Development, MakeOptions(origins: ["http://localhost:5173"], allowCredentials: true));

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [Trait("Category", "security")]
    public void Validate_WhenWildcardOriginWithCredentials_FailsInEveryEnvironment(string environmentName)
    {
        // Act
        var result = Validate(
            new FakeHostEnvironment(environmentName),
            MakeOptions(origins: ["*"], allowCredentials: true));

        // Assert
        using (new AssertionScope())
        {
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("*");
        }
    }

    private static ValidateOptionsResult Validate(IHostEnvironment environment, BasketCorsOptions options) =>
        new BasketCorsOptionsValidator(environment).Validate(name: null, options);

    private static IHostEnvironment Deployed => new FakeHostEnvironment("Production");

    private static IHostEnvironment Development => new FakeHostEnvironment("Development");

    private static BasketCorsOptions MakeOptions(string[] origins, bool allowCredentials) =>
        new()
        {
            AllowedOrigins = origins,
            AllowedMethods = ["GET"],
            AllowedHeaders = ["*"],
            AllowCredentials = allowCredentials,
        };

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Basket.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
