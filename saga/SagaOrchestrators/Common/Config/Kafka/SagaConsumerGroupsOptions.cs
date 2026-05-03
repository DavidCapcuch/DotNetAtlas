using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config.Kafka;

/// <summary>
/// Topic configuration for saga events.
/// </summary>
public sealed class SagaConsumerGroupsOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string PaymentProcessingSaga { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string AlertSubscriptionPurchaseSaga { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string AlertSubscriptionExtensionSaga { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string CheckoutSaga { get; set; }
}
