using System.Diagnostics;
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

    [Fact]
    public async Task AddFeatureFlags_OtelHook_FiresOnEvaluation()
    {
        // Arrange — seed one flag, then capture Activity events fired during evaluation to prove
        // OtelEvaluationHook is actually attached to Api.Instance (regression guard for the
        // AddHook-vs-AddSingleton bug caught in M3 Opus pre-commit review).
        await File.WriteAllTextAsync(_tempFile, """
            {
              "flags": {
                "catalog.show-discontinued": {
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

        var lifecycle = provider.GetRequiredService<OpenFeature.Hosting.IFeatureLifecycleManager>();
        await lifecycle.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        using var source = new ActivitySource("Test.FeatureFlagsHook");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Test.FeatureFlagsHook",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("flag-evaluation-scope")!;

        // Act
        var client = Api.Instance.GetClient();
        _ = await client.GetBooleanValueAsync(
            "catalog.show-discontinued",
            defaultValue: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        activity.Events.Should().Contain(e => e.Name == OtelEvaluationHook.EventName);
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
