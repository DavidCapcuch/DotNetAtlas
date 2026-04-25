using KafkaFlow;
using NetArchTest.Rules;

namespace Ordering.ArchitectureTests.Infrastructure;

public sealed class KafkaMessageHandlerTests : BaseTest
{
    [Fact]
    public void KafkaMessageHandlers_Should_HaveNameEndingWith_KafkaHandler()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ImplementInterface(typeof(IMessageHandler<>))
            .Should().HaveNameEndingWith("KafkaHandler")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "KafkaFlow message handlers follow the *KafkaHandler suffix");
    }

    [Fact]
    public void KafkaMessageHandlers_Should_LiveIn_SagaCommandsNamespace()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That().ImplementInterface(typeof(IMessageHandler<>))
            .Should().ResideInNamespace("Ordering.Infrastructure.Messaging.Kafka.SagaCommands")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Saga-command consumers are co-located in *.Messaging.Kafka.SagaCommands " +
            "per architecture-tests.md § 2.3 + use-cases.md § 3.3");
    }
}
