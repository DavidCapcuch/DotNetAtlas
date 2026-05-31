using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config;

/// <summary>
/// Timeout configuration for the Checkout saga. Values are in seconds (not minutes - the
/// Checkout saga's per-step timeouts are far shorter than PaymentProcessingSaga's because
/// they bracket local DB writes plus a fan-out, not external gateway round-trips).
/// </summary>
/// <remarks>
/// Per docs/bc-design/checkout-saga.md § 7 + § 7.2: the happy-path stack
/// (OrderCreation + StockReservation + Payment + OrderConfirmation = 30+60+90+30 = 210s)
/// plus 2 × CompensationSeconds must stay well under Inventory's reservation TTL
/// (default 900s / 15 min). The cross-BC invariant test enforces this on every build.
/// </remarks>
public sealed class CheckoutSagaTimeoutOptions
{
    public const string Section = $"{SagaOptions.Section}:CheckoutTimeouts";

    /// <summary>
    /// Timeout in seconds for OrderCreatedEvent to arrive after CreateOrderCommand is dispatched.
    /// Default 30 - Ordering does a local DB write; p99 should be well under 5s.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int OrderCreationSeconds { get; set; }

    /// <summary>
    /// Timeout in seconds for all per-line StockReservedEvents to arrive after fan-out.
    /// Default 60 - Inventory is event-sourced; allow headroom for slow consumer processing.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int StockReservationSeconds { get; set; }

    /// <summary>
    /// Timeout in seconds for PaymentCompletedEvent to arrive after RequestPaymentCommand.
    /// Default 90 - bracket gateway p99 latency. Must be >= sub-saga's
    /// AuthorizationMinutes + CaptureMinutes to avoid the captured-but-compensated race.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int PaymentSeconds { get; set; }

    /// <summary>
    /// Timeout in seconds for OrderConfirmedEvent to arrive after ConfirmOrderCommand.
    /// Default 30 - local DB write at Ordering; similar to OrderCreation.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int OrderConfirmationSeconds { get; set; }

    /// <summary>
    /// Timeout in seconds for compensation to complete. Default 300 - allows up to N stock
    /// releases + cancel-order, or refund + N releases + cancel-order, with retry headroom.
    /// Beyond this CompensationStuck fires and ops takes over.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int CompensationSeconds { get; set; }
}
