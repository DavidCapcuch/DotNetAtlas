using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability;

/// <summary>
/// Activity tags specific to the Payment Processing saga.
/// For common tags, see <see cref="SagaActivityTags"/>.
/// </summary>
public static class PaymentSagaActivityTags
{
    /// <summary>
    /// The payment amount.
    /// </summary>
    public const string Amount = "saga.amount";

    /// <summary>
    /// The currency code (e.g., "USD", "EUR").
    /// </summary>
    public const string Currency = "saga.currency";

    /// <summary>
    /// The authorization ID from the payment provider.
    /// </summary>
    public const string AuthorizationId = "saga.authorization_id";

    /// <summary>
    /// Whether the failed operation is retryable.
    /// </summary>
    public const string IsRetryable = "saga.is_retryable";

    /// <summary>
    /// The stage at which a timeout occurred (e.g., "authorization", "capture").
    /// </summary>
    public const string TimeoutStage = "saga.timeout_stage";

    /// <summary>
    /// Permitted values for the <see cref="TimeoutStage"/> tag and for the
    /// <c>stage</c> dimension of <c>PaymentProcessingSagaMetrics.RecordSagaTimeout</c>.
    /// Kept colocated with the tag key so trace and metric label cardinality stay in lockstep.
    /// </summary>
    public static class TimeoutStages
    {
        public const string Authorization = "authorization";
        public const string CaptureApproval = "capture_approval";
        public const string Capture = "capture";
        public const string Void = "void";
    }
}
