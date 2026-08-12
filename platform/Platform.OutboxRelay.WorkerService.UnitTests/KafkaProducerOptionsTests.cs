using System.Globalization;
using System.Reflection;
using Confluent.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.OutboxRelay.WorkerService.Common;
using Platform.OutboxRelay.WorkerService.OutboxRelay.Config;

namespace Platform.OutboxRelay.WorkerService.UnitTests;

/// <summary>
/// Pins how <see cref="KafkaProducerOptions"/> reaches librdkafka — the silent failure modes its
/// own remarks describe.
/// </summary>
/// <remarks>
/// Bound from the shipped <c>appsettings.json</c> rather than a hand-written fixture, so a key that
/// names no real setting is caught in the file that deploys. This covers the JSON layer only: the
/// compose relays additionally override some settings through <c>KafkaProducer__*</c> environment
/// variables, which no test here reads.
/// </remarks>
public class KafkaProducerOptionsTests
{
    /// <summary>
    /// Settings the relay's delivery semantics depend on, so absence must stop startup rather than
    /// silently downgrade at-least-once delivery.
    /// </summary>
    public static TheoryData<string> RequiredSettings =>
    [
        nameof(ProducerConfig.BootstrapServers),
        nameof(ProducerConfig.ClientId),
        nameof(ProducerConfig.Acks),
        nameof(ProducerConfig.EnableIdempotence),
        nameof(ProducerConfig.CompressionType),
    ];

    [Fact]
    public void Bind_EveryConfiguredKey_NamesARealProducerConfigSetting()
    {
        // Arrange
        var configured = ShippedSection().GetChildren().Select(child => child.Key);

        // Act
        var unknown = configured
            .Where(key => typeof(ProducerConfig).GetProperty(key) is null)
            .ToList();

        // Assert
        unknown.Should().BeEmpty(
            "a key under '{0}' that matches no ProducerConfig property binds to nothing and is " +
            "discarded without an error, leaving librdkafka on its own default",
            KafkaProducerOptions.Section);
    }

    [Fact]
    public void Bind_FromShippedConfiguration_DeliversEverySettingToLibrdkafka()
    {
        // Arrange
        var section = ShippedSection();

        // Act
        var options = Bind(ShippedConfiguration());

        // Assert
        using (new AssertionScope())
        {
            foreach (var setting in section.GetChildren())
            {
                // A key naming no setting is the other test's mutant; skipping it keeps each
                // failure here attributable to a value that did not arrive.
                if (typeof(ProducerConfig).GetProperty(setting.Key) is not { } property)
                {
                    continue;
                }

                // Read through the base declaration: its getter projects the string dictionary
                // ProducerBuilder enumerates, so a value missing from there reads back as null.
                Convert.ToString(property.GetValue(options), CultureInfo.InvariantCulture)
                    .Should().BeEquivalentTo(
                        setting.Value, "'{0}' must reach the dictionary verbatim", setting.Key);
            }
        }
    }

    [Fact]
    public void KafkaProducerOptions_DeclaresNoPropertyHidingAProducerConfigSetting()
    {
        // Act
        var shadowing = typeof(KafkaProducerOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => typeof(ProducerConfig).GetProperty(property.Name) is not null)
            .Select(property => property.Name)
            .ToList();

        // Assert
        shadowing.Should().BeEmpty(
            "a 'new' auto-property writes a CLR backing field while ProducerBuilder reads the base " +
            "string dictionary; the reflection binder happens to populate both, so the values still " +
            "arrive today, but the configuration-binding source generator, trimming or AOT would " +
            "leave the dictionary empty and the relay silently on librdkafka defaults");
    }

    [Fact]
    public void Clone_WhenTheCopyIsMutated_LeavesTheSourceUnchanged()
    {
        // Arrange
        var source = Bind(ShippedConfiguration());
        var sourceLingerMs = ((ProducerConfig)source).LingerMs;

        // Act
        var copy = source.Clone();
        ((ProducerConfig)copy).LingerMs = 4242;

        // Assert
        using (new AssertionScope())
        {
            ((ProducerConfig)source).LingerMs.Should().Be(
                sourceLingerMs, "the copy must not share the source's settings dictionary");
            ((ProducerConfig)copy).LingerMs.Should().Be(4242);
            ((ProducerConfig)copy).BootstrapServers.Should().Be(
                ((ProducerConfig)source).BootstrapServers, "the copy must carry the source's settings");
        }
    }

    [Theory]
    [MemberData(nameof(RequiredSettings))]
    public void Validate_WhenARequiredSettingIsMissing_FailsNamingTheSetting(string missingSetting)
    {
        // Arrange
        var options = Bind(ShippedConfigurationWithout(missingSetting));

        // Act
        var result = new KafkaProducerOptionsValidator().Validate(name: null, options);

        // Assert
        using (new AssertionScope())
        {
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain(missingSetting);
        }
    }

    [Fact]
    public void Validate_WhenIdempotenceIsDisabled_FailsNamingTheSetting()
    {
        // Arrange — the one contradictory producer setting librdkafka accepts. Every other
        // combination it refuses to build, so this is the only way to lose the relay's
        // per-partition de-duplication and ordering without anything saying so.
        var options = Bind(ShippedConfigurationWith(nameof(ProducerConfig.EnableIdempotence), "false"));

        // Act
        var result = new KafkaProducerOptionsValidator().Validate(name: null, options);

        // Assert
        using (new AssertionScope())
        {
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain(nameof(ProducerConfig.EnableIdempotence));
        }
    }

    [Fact]
    public void Validate_WithTheShippedConfiguration_Succeeds()
    {
        // Act
        var result = new KafkaProducerOptionsValidator().Validate(name: null, Bind(ShippedConfiguration()));

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AddOutboxRelayWorker_WhenARequiredSettingIsMissing_FailsOptionsResolution()
    {
        // Arrange — the real registration helper, so that dropping the validator's registration
        // (not just the guard inside it) reds this test.
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddConfiguration(ShippedConfigurationWithout(nameof(ProducerConfig.Acks)));
        builder.AddOutboxRelayWorker();
        using var provider = builder.Services.BuildServiceProvider();

        // Act — through IStartupValidator, so downgrading AddOptionsWithValidateOnStart to
        // AddOptions also reds this: that is what turns a misconfiguration into a refused boot
        // rather than a crash on the relay's first poll.
        var validateOnStart = () => provider.GetRequiredService<IStartupValidator>().Validate();

        // Assert
        validateOnStart.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{nameof(ProducerConfig.Acks)}*");
    }

    private static IConfiguration ShippedConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

    private static IConfigurationSection ShippedSection() =>
        ShippedConfiguration().GetRequiredSection(KafkaProducerOptions.Section);

    /// <summary>
    /// The shipped configuration with one producer setting overridden — a null <paramref name="value"/>
    /// removes it — plus the two relay settings <c>appsettings.json</c> deliberately leaves undefaulted
    /// (each deployment supplies its own schema via <c>OutboxRelay__SchemaName</c>, which keeps a
    /// misconfigured container fail-closed). Supplying those two leaves the producer setting as the only
    /// validation failure under test, so an assertion on it cannot pass on an unrelated failure's strength.
    /// </summary>
    private static IConfiguration ShippedConfigurationWith(string setting, string? value) =>
        new ConfigurationBuilder()
            .AddConfiguration(ShippedConfiguration())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.SchemaName)}"] = "saga",
                [$"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.TableName)}"] = "outbox_messages",
                [$"{KafkaProducerOptions.Section}:{setting}"] = value,
            })
            .Build();

    private static IConfiguration ShippedConfigurationWithout(string omittedSetting) =>
        ShippedConfigurationWith(omittedSetting, value: null);

    private static KafkaProducerOptions Bind(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddOptions<KafkaProducerOptions>().BindConfiguration(KafkaProducerOptions.Section);

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<KafkaProducerOptions>>().Value;
    }
}
