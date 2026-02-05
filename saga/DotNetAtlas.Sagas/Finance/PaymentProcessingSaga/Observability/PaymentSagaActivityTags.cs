using DotNetAtlas.Sagas.Common.Observability.Tracing;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability;

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
}
