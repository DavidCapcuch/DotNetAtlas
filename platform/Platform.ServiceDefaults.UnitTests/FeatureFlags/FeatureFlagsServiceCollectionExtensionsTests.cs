using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenFeature;
using Platform.ServiceDefaults.FeatureFlags;

namespace Platform.ServiceDefaults.UnitTests.FeatureFlags;

public class FeatureFlagsServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _tempFile;

    public FeatureFlagsServiceCollectionExtensionsTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), "platform-ff-" + Guid.CreateVersion7().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void AddFeatureFlags_RegistersFeatureApi()
    {
        // Arrange
        File.WriteAllText(_tempFile, "{\"flags\":{}}");
        var config = BuildConfig(_tempFile);
        var services = BuildServices(config);

        // Act
        services.AddFeatureFlags(config);
        using var provider = services.BuildServiceProvider();

        // Assert — DI contract: OpenFeatureBuilder completed (Api.Instance accessible).
        Api.Instance.Should().NotBeNull();
    }

    [Fact]
    public async Task AddFeatureFlags_WithLoadedFlagsJson_EvaluatesFlag()
    {
        // Arrange
        await File.WriteAllTextAsync(_tempFile, """
            {
              "flags": {
                "bff.eager-cache-warm": {
                  "state": "ENABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "on"
                }
              }
            }
            """, TestContext.Current.CancellationToken);
        var config = BuildConfig(_tempFile);
        var services = BuildServices(config);
        services.AddFeatureFlags(config);
        using var provider = services.BuildServiceProvider();

        // Force the OpenFeature hosting pipeline to initialize the configured provider so flag
        // evaluations return the file-seeded values rather than the NoOp default.
        var lifecycle = provider.GetRequiredService<OpenFeature.Hosting.IFeatureLifecycleManager>();
        await lifecycle.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        // Act
        var client = Api.Instance.GetClient();
        var value = await client.GetBooleanValueAsync(
            "bff.eager-cache-warm", defaultValue: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        value.Should().BeTrue();
    }

    private static ServiceCollection BuildServices(IConfiguration? config = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (config is not null)
        {
            services.AddSingleton(config);
        }

        return services;
    }

    private static IConfiguration BuildConfig(string filePath) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FilePath"] = filePath,
            })
            .Build();
}
