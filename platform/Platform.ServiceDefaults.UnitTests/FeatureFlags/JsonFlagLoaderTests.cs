using OpenFeature.Constant;
using OpenFeature.Providers.Memory;
using Platform.ServiceDefaults.FeatureFlags;

namespace Platform.ServiceDefaults.UnitTests.FeatureFlags;

public class JsonFlagLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public JsonFlagLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "platform-flags-" + Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_WithValidFile_ReturnsHydratedFlagDictionary()
    {
        // Arrange
        var path = WriteFlagFile("""
            {
              "flags": {
                "catalog.show-discontinued": {
                  "state": "ENABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "off"
                },
                "bff.eager-cache-warm": {
                  "state": "ENABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "on"
                }
              }
            }
            """);

        // Act
        var flags = JsonFlagLoader.Load(path);

        // Assert
        flags.Should().HaveCount(2);
        flags.Keys.Should().Contain(["catalog.show-discontinued", "bff.eager-cache-warm"]);
    }

    [Fact]
    public void Load_WithMissingFile_ReturnsEmptyDictionary()
    {
        // Arrange
        var missingPath = Path.Combine(_tempDir, "does-not-exist.json");

        // Act
        var flags = JsonFlagLoader.Load(missingPath);

        // Assert
        flags.Should().BeEmpty();
    }

    [Fact]
    public void Load_WithMalformedJson_ReturnsEmptyDictionary()
    {
        // Arrange
        var path = WriteFlagFile("{ not json at all }");

        // Act
        var flags = JsonFlagLoader.Load(path);

        // Assert
        flags.Should().BeEmpty();
    }

    [Fact]
    public void Load_FlagWithNonBooleanVariants_IsSkipped()
    {
        // Arrange
        var path = WriteFlagFile("""
            {
              "flags": {
                "region.rollout": {
                  "state": "ENABLED",
                  "variants": { "us": "us-east-1", "eu": "eu-west-1" },
                  "defaultVariant": "us"
                },
                "bff.eager-cache-warm": {
                  "state": "ENABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "on"
                }
              }
            }
            """);

        // Act
        var flags = JsonFlagLoader.Load(path);

        // Assert — v1 keeps only boolean flags; string-variant flag is skipped.
        flags.Keys.Should().BeEquivalentTo(["bff.eager-cache-warm"]);
    }

    [Fact]
    public void Load_FlagWithDefaultVariantNotInVariants_IsSkipped()
    {
        // Arrange
        var path = WriteFlagFile("""
            {
              "flags": {
                "broken.flag": {
                  "state": "ENABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "missing"
                }
              }
            }
            """);

        // Act
        var flags = JsonFlagLoader.Load(path);

        // Assert
        flags.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ENABLED", true)]
    [InlineData("enabled", true)]
    [InlineData("DISABLED", false)]
    [InlineData("NOT_A_STATE", false)]
    public async Task Load_FlagState_DecidesWhetherTheConfiguredVariantIsServed(
        string state,
        bool expectedValue)
    {
        // Arrange — defaultVariant is deliberately the opposite of the call-site default, so
        // "state honoured" and "state ignored" cannot produce the same answer. The lower-case row
        // is what fails if the comparison stops ignoring case.
        var path = WriteFlagFile($$"""
            {
              "flags": {
                "catalog.show-discontinued": {
                  "state": "{{state}}",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "on"
                }
              }
            }
            """);

        // Act
        var value = await ResolveAsync(path, "catalog.show-discontinued", callSiteDefault: false);

        // Assert
        value.Should().Be(expectedValue);
    }

    [Fact]
    public async Task Load_FlagWithNoStateProperty_IsTreatedAsEnabled()
    {
        // Arrange — state is optional, and its absence means enabled.
        var path = WriteFlagFile("""
            {
              "flags": {
                "catalog.show-discontinued": {
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "on"
                }
              }
            }
            """);

        // Act
        var value = await ResolveAsync(path, "catalog.show-discontinued", callSiteDefault: false);

        // Assert
        value.Should().BeTrue();
    }

    [Fact]
    public async Task Load_FlagWithMisspelledStateProperty_IsTreatedAsEnabled()
    {
        // Arrange — unknown properties are ignored, so "stat" leaves the state unset. Pinned
        // deliberately: the unrecognised-value fail-safe stops at the property name, and rejecting
        // unknown properties instead would fail the whole document and empty every flag.
        var path = WriteFlagFile("""
            {
              "flags": {
                "catalog.show-discontinued": {
                  "stat": "DISABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "on"
                }
              }
            }
            """);

        // Act
        var value = await ResolveAsync(path, "catalog.show-discontinued", callSiteDefault: false);

        // Assert
        value.Should().BeTrue();
    }

    [Fact]
    public async Task Load_DisabledFlag_ResolvesWithDisabledReasonRatherThanAnError()
    {
        // Arrange — pin the reason alongside the value: returning the right value for the wrong
        // reason (an error rather than a clean disable) is invisible to a value-only assertion.
        var path = WriteFlagFile("""
            {
              "flags": {
                "catalog.show-discontinued": {
                  "state": "DISABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "on"
                }
              }
            }
            """);
        var provider = new InMemoryProvider(JsonFlagLoader.Load(path));

        // Act
        var details = await provider.ResolveBooleanValueAsync(
            "catalog.show-discontinued", defaultValue: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var scope = new AssertionScope();
        details.Value.Should().BeFalse();
        details.Reason.Should().Be(OpenFeature.Constant.Reason.Disabled);
        details.ErrorType.Should().Be(ErrorType.None);
    }

    [Fact]
    public async Task Load_EnabledFlag_ResolvesTheConfiguredDefaultVariant()
    {
        // Arrange — a loader that picked an arbitrary variant key instead of defaultVariant would
        // otherwise go unnoticed.
        var path = WriteFlagFile("""
            {
              "flags": {
                "catalog.show-discontinued": {
                  "state": "ENABLED",
                  "variants": { "on": true, "off": false },
                  "defaultVariant": "off"
                }
              }
            }
            """);
        var provider = new InMemoryProvider(JsonFlagLoader.Load(path));

        // Act
        var details = await provider.ResolveBooleanValueAsync(
            "catalog.show-discontinued", defaultValue: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — "off" resolves false even though the call site asked for true.
        using var scope = new AssertionScope();
        details.Value.Should().BeFalse();
        details.Variant.Should().Be("off");
    }

    [Fact]
    public void Load_WithJsonObjectCarryingNoFlagsProperty_ReturnsEmptyDictionary()
    {
        // Arrange — syntactically valid JSON, so the malformed-file catch never sees it.
        var path = WriteFlagFile("{}");

        // Act
        var flags = JsonFlagLoader.Load(path);

        // Assert
        flags.Should().BeEmpty();
    }

    [Fact]
    public void Load_WithJsonNullDocument_ReturnsEmptyDictionary()
    {
        // Arrange — deserializes to a null document rather than to an object with null Flags.
        var path = WriteFlagFile("null");

        // Act
        var flags = JsonFlagLoader.Load(path);

        // Assert
        flags.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_WithBlankFilePath_ThrowsArgumentException(string filePath)
    {
        // Act
        var load = () => JsonFlagLoader.Load(filePath);

        // Assert — a blank path is a misconfigured service, not unreadable flag content, so it
        // fails loudly rather than degrading to an empty flag set.
        load.Should().Throw<ArgumentException>();
    }

    private static async Task<bool> ResolveAsync(string path, string flagKey, bool callSiteDefault)
    {
        var provider = new InMemoryProvider(JsonFlagLoader.Load(path));
        var details = await provider.ResolveBooleanValueAsync(
            flagKey, callSiteDefault, cancellationToken: TestContext.Current.CancellationToken);
        return details.Value;
    }

    private string WriteFlagFile(string contents)
    {
        var path = Path.Combine(_tempDir, "flags.json");
        File.WriteAllText(path, contents);
        return path;
    }
}
