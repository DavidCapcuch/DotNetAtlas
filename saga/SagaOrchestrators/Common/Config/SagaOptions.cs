using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config;

/// <summary>
/// Configuration options for saga orchestration.
/// </summary>
public sealed class SagaOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string Section = "Saga";

    /// <summary>
    /// Timeout configuration for the payment processing saga.
    /// </summary>
    [Required]
    public required PaymentProcessingSagaTimeoutOptions PaymentProcessingTimeouts { get; set; }

    /// <summary>
    /// Maximum number of retry attempts for saga operations.
    /// </summary>
    [Required]
    [Range(0, 10)]
    public required int MaxRetryAttempts { get; set; }

    /// <summary>
    /// Delay in seconds between retry attempts.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int RetryDelaySeconds { get; set; }

    /// <summary>
    /// Number of concurrent saga instances to process.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int ConcurrencyLimit { get; set; }
}
