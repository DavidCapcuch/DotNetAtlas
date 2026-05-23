using KafkaFlow;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using NetArchTest.Rules;

namespace Notifications.ArchitectureTests.Infrastructure;

/// <summary>
/// Architecture tests for Kafka message producers.
/// </summary>
public class KafkaProducerTests : BaseTest
{
    /// <summary>
    /// Convention for easy discovery.
    /// </summary>
    [Fact]
    public void KafkaProducers_Should_HaveNameEndingWith_KafkaProducer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .MeetCustomRule(new HasKafkaFlowMessageProducerDependencyRule())
            .Should()
            .HaveNameEndingWith("KafkaProducer")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Classes that depend on IMessageProducer<T> should follow the naming convention '*KafkaProducer' " +
            "for easy discovery and to clearly identify Kafka-specific infrastructure");
    }

    /// <summary>
    /// Detects KafkaFlow IMessageProducer{T} dependency by checking constructor parameters or fields for the generic interface.
    /// </summary>
    private sealed class HasKafkaFlowMessageProducerDependencyRule : ICustomRule
    {
        private static readonly string MessageProducerTypeName = typeof(IMessageProducer<>).FullName!;

        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var field in type.Fields)
            {
                if (IsMessageProducerType(field.FieldType))
                {
                    return true;
                }
            }

            foreach (var constructor in type.GetConstructors())
            {
                foreach (var parameter in constructor.Parameters)
                {
                    if (IsMessageProducerType(parameter.ParameterType))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsMessageProducerType(TypeReference typeReference)
        {
            if (typeReference is GenericInstanceType genericType)
            {
                return genericType.ElementType.FullName == MessageProducerTypeName;
            }

            return typeReference.FullName.StartsWith(MessageProducerTypeName, StringComparison.Ordinal);
        }
    }
}
