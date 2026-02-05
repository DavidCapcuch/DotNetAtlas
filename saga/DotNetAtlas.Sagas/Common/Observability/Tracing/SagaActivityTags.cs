namespace DotNetAtlas.Sagas.Common.Observability.Tracing;

/// <summary>
/// Common activity tags used across all saga types.
/// These tags are added to activities via <see cref="System.Diagnostics.Activity.SetTag"/>.
/// </summary>
public static class SagaActivityTags
{
    /// <summary>
    /// The correlation ID of the saga instance.
    /// </summary>
    public const string CorrelationId = "saga.correlation_id";

    /// <summary>
    /// The user ID associated with the saga.
    /// </summary>
    public const string UserId = "saga.user_id";

    /// <summary>
    /// The type of saga (e.g., "purchase", "extension", "payment").
    /// </summary>
    public const string Type = "saga.type";

    /// <summary>
    /// Error code from a failed operation.
    /// </summary>
    public const string ErrorCode = "saga.error_code";

    /// <summary>
    /// Error message from a failed operation.
    /// </summary>
    public const string ErrorMessage = "saga.error_message";

    /// <summary>
    /// Duration of the saga in milliseconds.
    /// </summary>
    public const string DurationMs = "saga.duration_ms";

    /// <summary>
    /// Whether compensation should be triggered.
    /// </summary>
    public const string ShouldCompensate = "saga.should_compensate";

    /// <summary>
    /// The payment transaction ID.
    /// </summary>
    public const string PaymentTransactionId = "saga.payment_transaction_id";

    /// <summary>
    /// The refund transaction ID.
    /// </summary>
    public const string RefundTransactionId = "saga.refund_transaction_id";

    /// <summary>
    /// Duration of the payment operation in milliseconds.
    /// </summary>
    public const string PaymentDurationMs = "saga.payment_duration_ms";
}
