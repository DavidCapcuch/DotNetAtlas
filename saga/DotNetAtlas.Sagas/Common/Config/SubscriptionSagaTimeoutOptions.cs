using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Timeout configuration options for the subscription purchase saga.
/// </summary>
public sealed class SubscriptionSagaTimeoutOptions
{
    /// <summary>
    /// Timeout in minutes for payment to complete before marking the saga as failed.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int PaymentMinutes { get; set; }

    /// <summary>
    /// Timeout in minutes for activation to complete before marking the saga as timed out.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int ActivationMinutes { get; set; }

    /// <summary>
    /// Timeout in minutes for compensation (refund) to complete before marking compensation as failed.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int CompensationMinutes { get; set; }
}

