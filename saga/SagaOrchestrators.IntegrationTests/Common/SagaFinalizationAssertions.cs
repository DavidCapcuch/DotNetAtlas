using AwesomeAssertions;
using AwesomeAssertions.Execution;
using AwesomeAssertions.Primitives;

namespace SagaOrchestrators.IntegrationTests.Common;

/// <summary>
/// Entry point for <c>finalization.Should().BeFinalized()</c>.
/// </summary>
public static class SagaFinalizationAssertionExtensions
{
    public static SagaFinalizationAssertions Should(this SagaFinalization instance)
        => new(instance, AssertionChain.GetOrCreate());
}

/// <summary>
/// Fluent assertions over a <see cref="SagaFinalization"/>. The failure message names the saga,
/// the timeout, and both the last-observed and current state, so a red test says where the saga
/// stalled without a debugger.
/// </summary>
public sealed class SagaFinalizationAssertions : ReferenceTypeAssertions<SagaFinalization, SagaFinalizationAssertions>
{
    private readonly AssertionChain _assertionChain;

    public SagaFinalizationAssertions(SagaFinalization subject, AssertionChain assertionChain)
        : base(subject, assertionChain)
    {
        _assertionChain = assertionChain;
    }

    protected override string Identifier => "saga finalization";

    /// <summary>
    /// Asserts the saga was finalized — its row removed — within the timeout the wait was given.
    /// </summary>
    public AndConstraint<SagaFinalizationAssertions> BeFinalized(
        string because = "",
        params object[] becauseArgs)
    {
        _assertionChain
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject.IsFinalized)
            .FailWith(
                "Expected saga {0} with CorrelationId {1} to be finalized within {2}{reason}, "
                + "but it was last observed in {3} and is still in {4}.",
                Subject.SagaType,
                Subject.CorrelationId,
                Subject.Timeout,
                Subject.LastObservedState,
                Subject.CurrentState);

        return new AndConstraint<SagaFinalizationAssertions>(this);
    }
}
