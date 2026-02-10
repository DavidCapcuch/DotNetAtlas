using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config.Kafka;

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
}
