namespace DotNetAtlas.Sagas.Common;

/// <summary>
/// Kafka consumer group name constants for saga consumers.
/// </summary>
public static class KafkaConsumerGroupBuilder
{
    /// <summary>
    /// Consumer group suffix for purchase saga consumers.
    /// </summary>
    public const string SubscriptionPurchase = "SubscriptionPurchase";

    /// <summary>
    /// Consumer group suffix for extension saga consumers.
    /// </summary>
    public const string SubscriptionExtension = "SubscriptionExtension";

    /// <summary>
    /// Consumer group suffix for payment saga consumers.
    /// </summary>
    public const string Payment = "Payment";

    /// <summary>
    /// Builds a consumer group name from the base group and saga-specific suffix.
    /// </summary>
    /// <param name="baseGroup">The base consumer group name from options.</param>
    /// <param name="sagaType">The saga type (e.g., "purchase", "extension", "payment").</param>
    /// <param name="eventName">The event name suffix.</param>
    /// <returns>The full consumer group name.</returns>
    public static string Build(string baseGroup, string sagaType, string eventName)
        => $"{baseGroup}-{sagaType}-{eventName}";
}
