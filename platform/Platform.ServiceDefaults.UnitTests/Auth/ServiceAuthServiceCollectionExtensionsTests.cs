using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;
using Platform.SharedKernel.Time;

namespace Platform.ServiceDefaults.UnitTests.Auth;

public class ServiceAuthServiceCollectionExtensionsTests
{
    [Fact]
    public void AddServiceAuth_RegistersOptionsHandlerAndTokenEndpointHttpClient()
    {
        // Arrange
        var services = BuildServicesWithConfig();

        // Act
        services.AddServiceAuth("catalog-service");
        using var provider = services.BuildServiceProvider();

        // Assert
        using var _ = new AssertionScope();
        var options = provider.GetRequiredService<IOptions<ServiceAuthOptions>>().Value;
        options.Authority.Should().Be("http://keycloak/realms/test");
        options.ClientId.Should().Be("catalog-service");
        options.ServiceName.Should().Be("catalog-service");

        provider.GetRequiredService<ClientCredentialsTokenHandler>().Should().NotBeNull();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var tokenClient = factory.CreateClient(ServiceAuthOptions.TokenEndpointHttpClientName);
        tokenClient.Should().NotBeNull();
    }

    [Fact]
    public void AddServiceAuth_HttpClientBuilder_AttachesHandler()
    {
        // Arrange
        var services = BuildServicesWithConfig();
        services.AddServiceAuth("catalog-service");

        // Act
        services.AddHttpClient("inventory-client").AddServiceAuth("inventory.read");
        using var provider = services.BuildServiceProvider();

        // Assert — creating the client via the factory resolves the handler chain without throwing.
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("inventory-client");
        client.Should().NotBeNull();
    }

    private static ServiceCollection BuildServicesWithConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceAuth:Authority"] = "http://keycloak/realms/test",
                ["ServiceAuth:ClientId"] = "catalog-service",
                ["ServiceAuth:ClientSecret"] = "dev-secret",
                ["ServiceAuth:ServiceName"] = "catalog-service",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UtcNow));
        return services;
    }
}
