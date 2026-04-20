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

    private string WriteFlagFile(string contents)
    {
        var path = Path.Combine(_tempDir, "flags.json");
        File.WriteAllText(path, contents);
        return path;
    }
}
