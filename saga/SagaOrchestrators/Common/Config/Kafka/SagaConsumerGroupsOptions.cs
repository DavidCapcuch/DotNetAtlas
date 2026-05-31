using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config.Kafka;

/// <summary>
/// Consumer-group ids for the saga state machines hosted in this worker.
/// One group per state machine — the documented exception to the
/// one-group-per-service rule in <c>docs/bc-design/events-catalog.md § 3.1</c>,
/// because each MassTransitStateMachine here is its own logical service per
/// <c>docs/adr/0001-centralized-saga-orchestration.md</c>.
/// </summary>
public sealed class SagaConsumerGroupsOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string PaymentProcessingSaga { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string CheckoutSaga { get; set; }
}
