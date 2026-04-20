using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Idempotency;

namespace Platform.ServiceDefaults.UnitTests.Idempotency;

public class IdempotencyKeyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddIdempotencyKeyOutputCache_WithMissingConnstr_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        // Act
        var act = () => services.AddIdempotencyKeyOutputCache(config, "catalog-service");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Redis:Cache*");
    }

    [Fact]
    public void AddIdempotencyKeyOutputCache_WithEmptyConnstr_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis:Cache"] = "   ",
            })
            .Build();

        // Act
        var act = () => services.AddIdempotencyKeyOutputCache(config, "catalog-service");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddIdempotencyKeyOutputCache_WithConnstr_RegistersOutputCacheStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis:Cache"] = "redis-cache:6379",
            })
            .Build();

        // Act
        services.AddIdempotencyKeyOutputCache(config, "catalog-service");

        // Assert — DI registration present; actual Redis handshake is integration-test territory.
        var registered = services.Any(d => d.ServiceType == typeof(IOutputCacheStore));
        registered.Should().BeTrue();
    }
}
