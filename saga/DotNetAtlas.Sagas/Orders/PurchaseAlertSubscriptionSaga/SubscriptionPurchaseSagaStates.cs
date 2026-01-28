namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga;

/// <summary>
/// Constants for subscription purchase saga state names.
/// These constants match the state property names in <see cref="SubscriptionPurchaseSaga"/>.
/// </summary>
public static class SubscriptionPurchaseSagaStates
{
    /// <summary>
    /// The saga is waiting for payment confirmation.
    /// </summary>
    public const string WaitingForPayment = nameof(SubscriptionPurchaseSaga.WaitingForPayment);

    /// <summary>
    /// Payment has failed.
    /// </summary>
    public const string PaymentFailed = nameof(SubscriptionPurchaseSaga.PaymentFailed);

    /// <summary>
    /// Payment is complete, waiting for subscription activation.
    /// </summary>
    public const string AwaitingActivation = nameof(SubscriptionPurchaseSaga.AwaitingActivation);

    /// <summary>
    /// Subscription activation completed successfully.
    /// </summary>
    public const string ActivationCompleted = nameof(SubscriptionPurchaseSaga.ActivationCompleted);

    /// <summary>
    /// Subscription activation failed.
    /// </summary>
    public const string ActivationFailed = nameof(SubscriptionPurchaseSaga.ActivationFailed);

    /// <summary>
    /// Compensation (refund) is in progress.
    /// </summary>
    public const string CompensationInProgress = nameof(SubscriptionPurchaseSaga.CompensationInProgress);

    /// <summary>
    /// Compensation (refund) completed successfully.
    /// </summary>
    public const string CompensationCompleted = nameof(SubscriptionPurchaseSaga.CompensationCompleted);

    /// <summary>
    /// Compensation (refund) failed.
    /// </summary>
    public const string CompensationFailed = nameof(SubscriptionPurchaseSaga.CompensationFailed);

    /// <summary>
    /// Terminal states that indicate the saga has completed (successfully or with failure).
    /// Sagas in these states should not be considered "stuck".
    /// </summary>
    public static readonly string[] FinalStates =
    [
        ActivationCompleted,
        ActivationFailed,
        CompensationCompleted,
        CompensationFailed,
        PaymentFailed
    ];
}
