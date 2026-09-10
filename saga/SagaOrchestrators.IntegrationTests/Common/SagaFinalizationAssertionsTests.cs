using Xunit.Sdk;

namespace SagaOrchestrators.IntegrationTests.Common;

/// <summary>
/// Pins that <c>BeFinalized()</c> can actually fail. Every saga suite's finalization check runs
/// through it, so an assertion that silently passed would make all of them vacuous — the defect
/// this assertion replaced. Needs no infrastructure: <see cref="SagaFinalization"/> is a plain
/// record, so this class stays out of the container-backed test collection.
/// </summary>
public sealed class SagaFinalizationAssertionsTests
{
    private static SagaFinalization Outcome(bool isFinalized, string currentState) => new(
        isFinalized,
        SagaType: "PaymentProcessingSagaState",
        CorrelationId: Guid.Parse("01930000-0000-7000-8000-000000000001"),
        Timeout: TimeSpan.FromSeconds(10),
        LastObservedState: "VoidInProgress",
        CurrentState: currentState);

    [Fact]
    public void BeFinalized_WhenTheRowWentAway_Passes()
    {
        var act = () => Outcome(isFinalized: true, currentState: "gone").Should().BeFinalized();

        act.Should().NotThrow();
    }

    [Fact]
    public void BeFinalized_WhenTheSagaNeverFinalized_FailsNamingBothStates()
    {
        var act = () => Outcome(isFinalized: false, currentState: "VoidFailed").Should().BeFinalized();

        act.Should().Throw<XunitException>()
            .WithMessage("*PaymentProcessingSagaState*01930000-0000-7000-8000-000000000001*10s*")
            .WithMessage("*VoidInProgress*", "the state the saga was last seen in locates the stall")
            .WithMessage("*VoidFailed*", "the state it is in now is what the fresh read adds");
    }

    /// <summary>
    /// A swallowed failure here would make every call site that batches this with sibling
    /// assertions vacuous, which is the defect this assertion exists to prevent.
    /// </summary>
    [Fact]
    public void BeFinalized_InsideAnAssertionScope_IsReportedAlongsideSiblingFailures()
    {
        var act = () =>
        {
            using (new AssertionScope())
            {
                Outcome(isFinalized: false, currentState: "VoidFailed").Should().BeFinalized();
                "actual".Should().Be("a sibling expectation");
            }
        };

        act.Should().Throw<XunitException>()
            .WithMessage("*VoidInProgress*", "the finalization failure must survive the scope")
            .WithMessage("*a sibling expectation*", "and must not short-circuit its siblings");
    }

    [Fact]
    public void BeFinalized_WhenTheSagaNeverFinalized_CarriesTheCallersReason()
    {
        var act = () => Outcome(isFinalized: false, currentState: "VoidFailed")
            .Should().BeFinalized("the void path finalizes once PaymentVoided lands");

        act.Should().Throw<XunitException>().WithMessage("*the void path finalizes once PaymentVoided lands*");
    }
}
