using Confluent.SchemaRegistry;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNetAtlas.Infrastructure.Communication.Kafka.Config;
using Testcontainers.Kafka;

namespace DotNetAtlas.IntegrationTests.Common;

/// <summary>
/// Encapsulates Kafka and Schema Registry test containers with fluent configuration.
/// Provides a clean abstraction for integration tests requiring Kafka infrastructure.
/// </summary>
public sealed class KafkaTestContainer : IAsyncDisposable
{
    private readonly KafkaContainer _kafkaContainer;
    private IContainer? _schemaRegistryContainer;

    /// <summary>
    /// Gets the KafkaOptions for this container.
    /// This property is populated after StartAsync is called.
    /// </summary>
    public KafkaOptions KafkaOptions { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaTestContainer"/> class.
    /// </summary>
    public KafkaTestContainer()
    {
        _kafkaContainer = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.5.9")
            .WithName($"TestKafkaFixture-{Guid.NewGuid()}")
            .WithKRaft()
            .WithCleanUp(true)
            .Build();
    }

    public async Task StartAsync()
    {
        await _kafkaContainer.StartAsync();

        // Get the mapped port for the BROKER listener (port 9093 inside container)
        var brokerPort = _kafkaContainer.GetMappedPublicPort(9093);

        // Schema Registry connects to Kafka using the BROKER listener
        // See https://github.com/testcontainers/testcontainers-dotnet/blob/c27a94ba320cad698f7bd05b2f93856c0aebb088/src/Testcontainers.Kafka/KafkaBuilder.cs#L222-L223
        var schemaRegistryKafkaBootstrap = $"PLAINTEXT://host.docker.internal:{brokerPort}";

        // Build Schema Registry container AFTER Kafka is started
        _schemaRegistryContainer = new ContainerBuilder()
            .WithImage("confluentinc/cp-schema-registry:7.5.0")
            .WithName($"TestSchemaRegistryFixture-{Guid.NewGuid()}")
            .WithEnvironment("SCHEMA_REGISTRY_HOST_NAME", "schema-registry")
            .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS", schemaRegistryKafkaBootstrap)
            .WithPortBinding(8081, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8081).ForPath("/subjects")))
            .WithCleanUp(true)
            .Build();

        await _schemaRegistryContainer.StartAsync();

        var schemaRegistryUrl = $"http://localhost:{_schemaRegistryContainer.GetMappedPublicPort(8081)}";
        var bootstrapServers = _kafkaContainer.GetBootstrapAddress();

        KafkaOptions = CreateKafkaOptions(bootstrapServers, schemaRegistryUrl);
    }

    /// <summary>
    /// Creates a KafkaOptions from the container values.
    /// </summary>
    private KafkaOptions CreateKafkaOptions(string bootstrapServers, string schemaRegistryUrl)
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
        if (_schemaRegistryContainer != null)
        {
            await _schemaRegistryContainer.DisposeAsync();
        }

        await _kafkaContainer.DisposeAsync();
    }
}
