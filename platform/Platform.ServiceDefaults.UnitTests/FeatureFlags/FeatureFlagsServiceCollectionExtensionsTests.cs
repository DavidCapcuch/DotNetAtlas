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

        // Act — resolve from the container, not the static Api.Instance: AddOpenFeature registers an
        // isolated Api here, so a client off Api.Instance has no provider and every flag silently
        // falls through to the call-site default.
        var client = provider.GetRequiredService<IFeatureClient>();
        var details = await client.GetBooleanDetailsAsync(
            "bff.eager-cache-warm", defaultValue: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — pin the variant too, not just the value: a loader that picked an arbitrary variant
        // instead of defaultVariant would still return true here and survive a value-only assertion.
        details.Value.Should().BeTrue();
        details.Variant.Should().Be("on");
    }

    [Fact]
    public async Task AddFeatureFlags_LeavesTheProcessGlobalApiUnconfigured()
    {
        // Arrange — identical wiring to the test above; the subject here is *which* Api got the provider.
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
        await provider.GetRequiredService<OpenFeature.Hosting.IFeatureLifecycleManager>()
            .EnsureInitializedAsync(TestContext.Current.CancellationToken);

        // Act — the process-global singleton, deliberately, rather than the container's Api.
        var globalValue = await Api.Instance.GetClient().GetBooleanValueAsync(
            "bff.eager-cache-warm", defaultValue: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — AddFeatureFlags must bind the provider to a container-scoped Api and leave the
        // process-global one alone, so two hosts in one test process cannot contaminate each other's
        // flag reads. Nothing else in the suite pins this, and losing it would surface as cross-fixture
        // flakiness rather than as a failed assertion.
        globalValue.Should().BeFalse();
    }

    private static ServiceCollection BuildServices(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);

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
