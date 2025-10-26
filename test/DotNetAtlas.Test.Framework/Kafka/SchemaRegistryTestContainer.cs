using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using DotNetAtlas.Test.Framework.Common;

namespace DotNetAtlas.Test.Framework.Kafka;

public sealed class SchemaRegistryTestContainer : ITestContainer
{
    private readonly IContainer _container;

    public string ImageName => "confluentinc/cp-schema-registry:7.5.0";

    public string Url { get; private set; } = null!;

    public SchemaRegistryTestContainer(INetwork network, string kafkaBootstrapServer)
    {
        _container = new ContainerBuilder()
            .WithImage(ImageName)
            .WithName($"TestSchemaRegistryFixture-{Guid.NewGuid()}")
            .WithNetwork(network)
            .WithEnvironment("SCHEMA_REGISTRY_HOST_NAME", "schema-registry")
            .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS", kafkaBootstrapServer)
            .WithEnvironment("SCHEMA_REGISTRY_LISTENERS", "http://0.0.0.0:8081")
            .WithPortBinding(8081, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8081).ForPath("/subjects")))
            .WithCleanUp(true)
            .Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _container.StartAsync(cancellationToken);
        Url = $"http://localhost:{_container.GetMappedPublicPort(8081)}";
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
