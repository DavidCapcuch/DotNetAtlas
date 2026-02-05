using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Timeout configuration options for the payment processing saga.
/// </summary>
public sealed class PaymentProcessingSagaTimeoutOptions
{
    /// <summary>
    /// Timeout in minutes for payment authorization to complete.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int AuthorizationMinutes { get; set; }

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

    /// <summary>
    /// Timeout in minutes for subscription activation to complete after payment capture.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int ActivationMinutes { get; set; }

    /// <summary>
    /// Timeout in minutes for refund to complete (when compensation is needed after capture).
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int RefundMinutes { get; set; }

    /// <summary>
    /// Timeout in minutes after payment completion before the saga finalizes.
    /// This provides a window for calling sagas to request refunds if their downstream operations fail.
    /// After this timeout, the saga finalizes and late refunds must be handled through a separate service.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int SuccessFinalizationMinutes { get; set; }
}
