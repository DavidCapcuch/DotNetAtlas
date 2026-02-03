using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Topic configuration for saga events.
/// </summary>
public sealed class SagaConsumerGroupsOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string PaymentSaga { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string SubscriptionPurchaseSaga { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string SubscriptionExtensionSaga { get; set; }
}
