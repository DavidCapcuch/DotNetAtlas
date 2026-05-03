using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config.Kafka;

/// <summary>
/// Topic configuration for sagas.
/// </summary>
public sealed class SagaTopicsOptions
{
    public const string Section = $"{KafkaOptions.Section}:Topics";

    private const int MaximumKafkaTopicLength = 249;

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string WeatherAlertSubscriptions { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string WeatherAlertSubscriptionsCommands { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string PaymentsPayments { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string PaymentsPaymentCommands { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string BasketSessions { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string OrderingOrders { get; set; }

    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string InventoryReservations { get; set; }

    /// <summary>
    /// Gets all configured topics as an array.
    /// </summary>
    public string[] GetAllTopics()
    {
        return
        [
            WeatherAlertSubscriptions,
            WeatherAlertSubscriptionsCommands,
            PaymentsPayments,
            PaymentsPaymentCommands,
            BasketSessions,
            OrderingOrders,
            InventoryReservations
        ];
    }
}
