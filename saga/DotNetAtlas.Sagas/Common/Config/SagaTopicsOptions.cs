using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Topic configuration for sagas.
/// </summary>
public sealed class SagaTopicsOptions
{
    private const int MaximumKafkaTopicLength = 249;

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string OrderAlertSubscriptions { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string WeatherAlertSubscriptions { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string WeatherAlertSubscriptionsCommands { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string FinancePayments { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string FinancePaymentCommands { get; set; }
}
