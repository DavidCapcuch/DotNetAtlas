using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config;

/// <summary>
/// Timeout configuration options for the payment processing saga.
/// </summary>
public sealed class PaymentProcessingSagaTimeoutOptions
{
    public const string Section = $"{SagaOptions.Section}:PaymentProcessingTimeouts";

    /// <summary>
    /// Timeout in minutes for payment authorization to complete.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int AuthorizationMinutes { get; set; }

    /// <summary>
    /// Timeout in minutes for the Checkout saga to signal capture approval / abort after
    /// authorization (ADR-0026 capture-pivot wait-state). On expiry the sub-saga drives the void
    /// path so the dangling authorization is released rather than left open.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int CaptureApprovalMinutes { get; set; }

    /// <summary>
    /// Timeout in minutes for payment capture to complete.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int CaptureMinutes { get; set; }

    /// <summary>
    /// Timeout in minutes for the payment void to complete.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int VoidMinutes { get; set; }
}
