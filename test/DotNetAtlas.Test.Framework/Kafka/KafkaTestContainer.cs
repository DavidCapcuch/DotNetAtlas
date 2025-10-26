using Confluent.SchemaRegistry;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Test.Framework.Common;
using Testcontainers.Kafka;

namespace DotNetAtlas.Test.Framework.Kafka;

/// <summary>
/// Encapsulates Kafka and Schema Registry test containers.
/// Provides a clean abstraction for integration tests requiring Kafka infrastructure.
/// </summary>
/// <remarks>
/// Keep the container images in sync with production.
/// When upgrading infrastructure, update the images here early to catch breaking changes sooner.
/// </remarks>
public sealed class KafkaTestContainer : ITestContainer
{
    private readonly INetwork _network;
    private readonly KafkaContainer _kafkaContainer;
    private readonly SchemaRegistryTestContainer _schemaRegistryContainer;

    public string ImageName => "confluentinc/cp-kafka:7.5.9";

    /// <summary>
    /// Gets the KafkaOptions for this container.
    /// This property is populated after StartAsync is called.
    /// </summary>
    public KafkaOptions KafkaOptions { get; private set; } = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaTestContainer"/> class.
    /// </summary>
    public KafkaTestContainer()
    {
        // CI note: On Linux GitHub runners, containers cannot resolve host.docker.internal.
        // Kafka and Schema Registry need to have a defined network and use the
        // container aliases to ensure reliable inter-container connectivity in CI and locally.
        _network = new NetworkBuilder()
            .WithName($"TestKafkaNetwork-{Guid.NewGuid()}")
            .WithCleanUp(true)
            .Build();

        _kafkaContainer = new KafkaBuilder()
            .WithImage(ImageName)
            .WithName($"TestKafkaFixture-{Guid.NewGuid()}")
            .WithKRaft()
            .WithCleanUp(true)
            .WithNetwork(_network)
            .WithNetworkAliases("kafka")
            .Build();

        // Do NOT use host.docker.internal here; it is not available inside Linux containers with Docker
        // See 9093 is the default broker port testcontainers use
        // https://github.com/testcontainers/testcontainers-dotnet/blob/c2b86ad25b947cbd9e3b30dcce90f0d161607ada/src/Testcontainers.Kafka/KafkaBuilder.cs#L223C1-L224C1
        const string kafkaBootstrapServer = "PLAINTEXT://kafka:9093";
        _schemaRegistryContainer = new SchemaRegistryTestContainer(_network, kafkaBootstrapServer);
    }

    /// <summary>
    /// Starts the Kafka and Schema Registry containers.
    /// Call this during test fixture initialization (e.g., in PreSetupAsync or constructor).
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _kafkaContainer.StartAsync(ct);
        await _schemaRegistryContainer.StartAsync(ct);

        var schemaRegistryUrl = _schemaRegistryContainer.Url;
        var bootstrapServers = _kafkaContainer.GetBootstrapAddress();

        KafkaOptions = CreateKafkaOptions(bootstrapServers, schemaRegistryUrl);
    }

    /// <summary>
    /// Creates a KafkaOptions from the container values.
    /// </summary>
    private static KafkaOptions CreateKafkaOptions(string bootstrapServers, string schemaRegistryUrl)
    {
        return new KafkaOptions
        {
            Brokers =
            [
                bootstrapServers
            ],
            SchemaRegistry = new SchemaRegistryOptions
            {
                Url = schemaRegistryUrl
            },
            AvroSerializer = new AvroSerializerOptions
            {
                AutoRegisterSchemas = true,
                SubjectNameStrategy = SubjectNameStrategy.TopicRecord
            }
        };
    }

    /// <summary>
    /// Disposes the Kafka and Schema Registry containers.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _schemaRegistryContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
        await _network.DisposeAsync();
    }
}
