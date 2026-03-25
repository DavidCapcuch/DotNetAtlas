namespace Platform.KafkaFlow.ProducerHeaders;

/// <summary>
/// Configuration options for the producer headers middleware.
/// </summary>
public sealed class ProducerHeadersOptions
{
    /// <summary>
    /// The origin identifier to include in the Origin header.
    /// This identifies the service/application that produced the message.
    /// </summary>
    public required string Origin { get; set; }
}
