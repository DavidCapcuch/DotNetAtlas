using NetArchTest.Rules;

namespace Payments.ArchitectureTests.Infrastructure;

/// <summary>
/// Locks the Payments BC's "outbox-is-the-only-path" invariant per ADR-0001
/// (#258). Every external Kafka publish must flow through
/// <c>ITransactionalOutbox&lt;IPaymentsDbContext&gt;</c> + the
/// <c>outbox-relay-payments</c> container; direct <see cref="KafkaFlow.IProducer{TKey,TMessage}"/>
/// usage from <c>Payments.Application</c> / <c>Payments.Infrastructure</c> is forbidden because
/// it would bypass the transactional outbox and break the "no dual-write" guarantee. The
/// in-process DLT producer is registered with KafkaFlow's <c>IMessageProducer&lt;DeadLetterMiddleware&gt;</c>
/// (platform-internal), so it isn't covered by this rule.
/// </summary>
public sealed class OutboxOnlyPathTests : BaseTest
{
    [Fact]
    public void Application_ShouldNot_DependOn_KafkaFlowIProducer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny("KafkaFlow.IProducer`2", "KafkaFlow.IProducer")
            .GetResult();

        result.FailingTypes.Should().BeNullOrEmpty(
            "Application layer must publish exclusively through the transactional outbox port " +
            "ITransactionalOutbox<IPaymentsDbContext>. Direct KafkaFlow.IProducer<,> usage would " +
            "bypass the outbox and break the no-dual-write invariant (ADR-0001).");
    }

    [Fact]
    public void Infrastructure_ShouldNot_DependOn_KafkaFlowIProducer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny("KafkaFlow.IProducer`2", "KafkaFlow.IProducer")
            .GetResult();

        result.FailingTypes.Should().BeNullOrEmpty(
            "Infrastructure layer must not hold direct KafkaFlow.IProducer<,> references. " +
            "External event emission is via the transactional outbox + outbox-relay-payments " +
            "container; the only producer registration in this assembly is the in-process DLT " +
            "producer typed as IMessageProducer<DeadLetterMiddleware>, which is platform-internal.");
    }
}
