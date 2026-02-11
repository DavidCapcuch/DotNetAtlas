using MassTransit;

namespace DotNetAtlas.Sagas.Common.SagaAbstractions;

/// <summary>
/// Interface for saga state instances that have a current state property.
/// Extends MassTransit's <see cref="SagaStateMachineInstance"/> with the CurrentState property
/// mainly for usage in integration test helpers that need to poll for state transitions.
/// </summary>
public interface ISagaStateInstance : SagaStateMachineInstance
{
    /// <summary>
    /// Current state of the saga state machine.
    /// </summary>
    string CurrentState { get; set; }
}
