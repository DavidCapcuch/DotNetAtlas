using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Timeout configuration options for the payment processing saga.
/// </summary>
public sealed class PaymentSagaTimeoutOptions
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
    /// Timeout in minutes for payment void to complete.
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
}

