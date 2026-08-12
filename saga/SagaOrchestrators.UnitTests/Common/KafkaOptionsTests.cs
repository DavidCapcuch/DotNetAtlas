using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SagaOrchestrators.Common;
using SagaOrchestrators.Common.Config.Kafka;

namespace SagaOrchestrators.UnitTests.Common;

/// <summary>
/// Pins how the saga's Confluent-derived Kafka options reach the Schema Registry client and the
/// Avro serializer/deserializer — the silent failure modes those types' remarks describe.
/// </summary>
/// <remarks>
/// Bound from the production <c>saga/SagaOrchestrators/appsettings.json</c> (NOT the Testing
/// overlay) rather than a hand-written fixture, so a key that names no real setting is caught in the
/// file that deploys.
/// </remarks>
public sealed class KafkaOptionsTests
{
    /// <summary>
    /// Each Confluent-derived options type paired with the configuration section it binds from.
    /// </summary>
    public static TheoryData<string, Type> BoundSections =>
        new()
        {
            { SagaSchemaRegistryOptions.Section, typeof(SagaSchemaRegistryOptions) },
            { AvroSerializerOptions.Section, typeof(AvroSerializerOptions) },
            { AvroDeserializerOptions.Section, typeof(AvroDeserializerOptions) },
        };

    /// <summary>
    /// Settings that live on a Confluent base class, so no data annotation can reach them. Absence
    /// must stop startup rather than leave the client on a vendor default nobody chose.
    /// </summary>
    public static TheoryData<string, Type> RequiredSettings =>
        new()
        {
            { $"{SagaSchemaRegistryOptions.Section}:Url", typeof(SagaSchemaRegistryOptions) },
            { $"{AvroSerializerOptions.Section}:SubjectNameStrategy", typeof(AvroSerializerOptions) },
            { $"{AvroSerializerOptions.Section}:AutoRegisterSchemas", typeof(AvroSerializerOptions) },
            { $"{AvroDeserializerOptions.Section}:SubjectNameStrategy", typeof(AvroDeserializerOptions) },
        };

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
                if (vendorBase.GetProperty(setting.Key) is not { } property)
                {
                    continue;
                }

                // Read through the base declaration: its getter projects the string dictionary the
                // Confluent serdes enumerate, so a value missing from there reads back as null.
                Convert.ToString(property.GetValue(options), CultureInfo.InvariantCulture)
                    .Should().BeEquivalentTo(
                        setting.Value, "'{0}' must reach the dictionary verbatim", setting.Key);
            }
        }
    }

    [Theory]
    [MemberData(nameof(RequiredSettings))]
    public void Validate_WhenARequiredSettingIsMissing_FailsNamingTheSetting(
        string missingSetting,
        Type optionsType)
    {
        // Arrange
        var section = missingSetting[..missingSetting.LastIndexOf(':')];
        var options = Bind(ShippedConfigurationWithout(missingSetting), section, optionsType);

        // Act
        var result = Validate(options);

        // Assert
        using (new AssertionScope())
        {
            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain(missingSetting.Split(':')[^1]);
        }
    }

    [Theory]
    [MemberData(nameof(BoundSections))]
    public void Validate_WithTheShippedConfiguration_Succeeds(string section, Type optionsType)
    {
        // Act
        var result = Validate(Bind(ShippedConfiguration(), section, optionsType));

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(RequiredSettings))]
    public void AddSagaOrchestration_WhenARequiredSettingIsMissing_FailsOptionsResolution(
        string missingSetting,
        Type optionsType)
    {
        // Arrange — the real registration path, so that dropping a validator's registration (not
        // just the guard inside it) reds this test.
        using var provider = Register(ShippedConfigurationWithout(missingSetting));

        // Act — through IStartupValidator, so downgrading AddOptionsWithValidateOnStart to
        // AddOptions also reds this: that is what turns a misconfiguration into a refused boot
        // rather than a saga consumer running on a vendor default.
        var validateOnStart = () => provider.GetRequiredService<IStartupValidator>().Validate();

        // Assert
        validateOnStart.Should().Throw<OptionsValidationException>(
                "{0} carries no data annotations, so only its validator can refuse the boot",
                optionsType.Name)
            .WithMessage($"*{missingSetting.Split(':')[^1]}*");
    }

    [Fact]
    public void AddSagaOrchestration_WithTheShippedConfiguration_PassesStartupValidation()
    {
        // Arrange — a clean baseline is what makes the theory above attributable: every failure
        // there must come from the setting it removed, not from an unrelated one.
        using var provider = Register(ShippedConfiguration());

        // Act
        var validateOnStart = () => provider.GetRequiredService<IStartupValidator>().Validate();

        // Assert
        validateOnStart.Should().NotThrow();
    }

    /// <summary>
    /// Runs the real registration path. <c>IConfiguration</c> is registered because
    /// <c>BindConfiguration</c> resolves it from the container — the host does this for free.
    /// </summary>
    private static ServiceProvider Register(ConfigurationManager configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSagaOrchestration(configuration, isClusterEnvironment: false);

        return services.BuildServiceProvider();
    }

    private static ValidateOptionsResult Validate(object options) => options switch
    {
        SagaSchemaRegistryOptions schemaRegistry =>
            new SagaSchemaRegistryOptionsValidator().Validate(name: null, schemaRegistry),
        AvroSerializerOptions serializer =>
            new AvroSerializerOptionsValidator().Validate(name: null, serializer),
        AvroDeserializerOptions deserializer =>
            new AvroDeserializerOptionsValidator().Validate(name: null, deserializer),
        _ => throw new ArgumentOutOfRangeException(nameof(options), options, "No validator for this type."),
    };

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
}
