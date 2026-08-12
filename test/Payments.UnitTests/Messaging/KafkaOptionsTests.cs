using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Payments.Infrastructure.Common;
using Payments.Infrastructure.Messaging.Kafka.Config;
using Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

namespace Payments.UnitTests.Messaging;

/// <summary>
/// Pins how Payments' Confluent-derived Kafka options reach librdkafka, the Schema Registry
/// client and the Avro serializer — the silent failure modes those types' remarks describe.
/// </summary>
/// <remarks>
/// Bound from the shipped <c>appsettings.json</c> rather than a hand-written fixture, so a key that
/// names no real setting is caught in the file that deploys. This covers the JSON layer only:
/// compose additionally overrides some settings through <c>Kafka__*</c> environment variables, which
/// no test here reads.
/// </remarks>
public class KafkaOptionsTests
{
    /// <summary>
    /// Each Confluent-derived options type paired with the configuration section it binds from.
    /// </summary>
    public static TheoryData<string, Type> BoundSections =>
        new()
        {
            { SchemaRegistryOptions.Section, typeof(SchemaRegistryOptions) },
            { AvroSerializerOptions.Section, typeof(AvroSerializerOptions) },
        };

    /// <summary>
    /// Settings that live on a Confluent base class, so no data annotation can reach them. Absence
    /// must stop startup rather than leave the client on a vendor default nobody chose.
    /// </summary>
    public static TheoryData<string> RequiredSettings =>
    [
        $"{SchemaRegistryOptions.Section}:Url",
        $"{AvroSerializerOptions.Section}:SubjectNameStrategy",
        $"{AvroSerializerOptions.Section}:AutoRegisterSchemas",
    ];

    [Theory]
    [MemberData(nameof(BoundSections))]
    public void Bind_EveryConfiguredKey_NamesARealSetting(string section, Type optionsType)
    {
        // Arrange
        var configured = ShippedConfiguration().GetRequiredSection(section).GetChildren();

        // Act
        var unknown = configured
            .Where(child => optionsType.GetProperty(child.Key) is null)
            .Select(child => child.Key)
            .ToList();

        // Assert
        unknown.Should().BeEmpty(
            "a key under '{0}' that matches no property on {1} or its Confluent base binds to " +
            "nothing and is discarded without an error, leaving the client on its own default",
            section,
            optionsType.Name);
    }

    [Theory]
    [MemberData(nameof(BoundSections))]
    public void Bind_FromShippedConfiguration_DeliversEverySettingToTheClient(string section, Type optionsType)
    {
        // Arrange
        var vendorBase = optionsType.BaseType!;
        var configured = ShippedConfiguration().GetRequiredSection(section).GetChildren();

        // Act
        var options = Bind(ShippedConfiguration(), section, optionsType);

        // Assert
        using (new AssertionScope())
        {
            foreach (var setting in configured)
            {
                // Only settings declared on the Confluent base travel through the string
                // dictionary the clients enumerate; anything declared on the options type itself
                // is an ordinary CLR property and is not part of that contract.
                if (vendorBase.GetProperty(setting.Key) is not { } property)
                {
                    continue;
                }

                // Read through the base declaration: its getter projects the string dictionary the
                // Confluent clients enumerate, so a value missing from there reads back as null.
                Convert.ToString(property.GetValue(options), CultureInfo.InvariantCulture)
                    .Should().BeEquivalentTo(
                        setting.Value, "'{0}' must reach the dictionary verbatim", setting.Key);
            }
        }
    }

    [Theory]
    [MemberData(nameof(RequiredSettings))]
    public void AddKafkaMessaging_WhenARequiredSettingIsMissing_FailsOptionsResolution(string missingSetting)
    {
        // Arrange — the real registration helper, so that dropping a validator's registration
        // (not just the guard inside it) reds this test.
        using var provider = Register(ShippedConfigurationWithout(missingSetting));

        // Act — through IStartupValidator, so downgrading AddOptionsWithValidateOnStart to
        // AddOptions also reds this: that is what turns a misconfiguration into a refused boot
        // rather than a consumer that runs on a vendor default.
        var validateOnStart = () => provider.GetRequiredService<IStartupValidator>().Validate();

        // Assert
        validateOnStart.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{missingSetting.Split(':')[^1]}*");
    }

    [Fact]
    public void AddKafkaMessaging_WithTheShippedConfiguration_PassesStartupValidation()
    {
        // Arrange — a clean baseline is what makes the theory above attributable: every failure
        // there must come from the setting it removed, not from an unrelated one.
        using var provider = Register(ShippedConfiguration());

        // Act
        var validateOnStart = () => provider.GetRequiredService<IStartupValidator>().Validate();

        // Assert
        validateOnStart.Should().NotThrow();
    }

    private static ConfigurationManager ShippedConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration.SetBasePath(AppContext.BaseDirectory);
        configuration.AddJsonFile("appsettings.json", optional: false);

        return configuration;
    }

    /// <summary>
    /// The shipped configuration with one setting removed — a later provider supplying a null value
    /// shadows the JSON one, which is how a deployment that simply omits the key behaves.
    /// </summary>
    private static ConfigurationManager ShippedConfigurationWithout(string omittedSetting)
    {
        var configuration = ShippedConfiguration();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [omittedSetting] = null,
        });

        return configuration;
    }

    private static object Bind(IConfiguration configuration, string section, Type optionsType) =>
        configuration.GetRequiredSection(section).Get(optionsType)!;

    /// <summary>
    /// Runs the real registration helper. <c>IConfiguration</c> is registered because
    /// <c>BindConfiguration</c> resolves it from the container — the host does this for free.
    /// </summary>
    private static ServiceProvider Register(ConfigurationManager configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKafkaMessaging(configuration);

        return services.BuildServiceProvider();
    }
}
