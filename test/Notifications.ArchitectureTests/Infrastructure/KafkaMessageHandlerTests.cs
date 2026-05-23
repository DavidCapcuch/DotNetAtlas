using KafkaFlow;
using NetArchTest.Rules;

namespace Notifications.ArchitectureTests.Infrastructure;

/// <summary>
/// Architecture tests for Kafka message handlers.
/// </summary>
public class KafkaMessageHandlerTests : BaseTest
{
    /// <summary>
    /// Convention for easy discovery.
    /// </summary>
    [Fact]
    public void KafkaMessageHandlers_Should_HaveNameEndingWith_KafkaHandler()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ImplementInterface(typeof(IMessageHandler<>))
            .Should()
            .HaveNameEndingWith("KafkaHandler")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Kafka message handlers should follow the naming convention '*KafkaHandler' " +
            "for easy discovery and to distinguish them from other handler types");
    }
}
