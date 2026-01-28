namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga;

/// <summary>
/// Constants for subscription extension saga state names.
/// These constants match the state property names in <see cref="SubscriptionExtensionSaga"/>.
/// </summary>
public static class SubscriptionExtensionSagaStates
{
    /// <summary>
    /// The saga is waiting for payment confirmation.
    /// </summary>
    public const string WaitingForPayment = nameof(SubscriptionExtensionSaga.WaitingForPayment);

    /// <summary>
    /// Payment has failed.
    /// </summary>
    public const string PaymentFailed = nameof(SubscriptionExtensionSaga.PaymentFailed);

    /// <summary>
    /// Payment is complete, waiting for subscription extension.
    /// </summary>
    public const string AwaitingExtension = nameof(SubscriptionExtensionSaga.AwaitingExtension);

    /// <summary>
    /// Subscription extension completed successfully.
    /// </summary>
    public const string ExtensionCompleted = nameof(SubscriptionExtensionSaga.ExtensionCompleted);

    /// <summary>
    /// Subscription extension failed.
    /// </summary>
    public const string ExtensionFailed = nameof(SubscriptionExtensionSaga.ExtensionFailed);

    /// <summary>
    /// Compensation (refund) is in progress.
    /// </summary>
    public const string CompensationInProgress = nameof(SubscriptionExtensionSaga.CompensationInProgress);

    /// <summary>
    /// Compensation (refund) completed successfully.
    /// </summary>
    public const string CompensationCompleted = nameof(SubscriptionExtensionSaga.CompensationCompleted);

    /// <summary>
    /// Compensation (refund) failed.
    /// </summary>
    public const string CompensationFailed = nameof(SubscriptionExtensionSaga.CompensationFailed);

    /// <summary>
    /// Terminal states that indicate the saga has completed (successfully or with failure).
    /// Sagas in these states should not be considered "stuck".
    /// </summary>
    public static readonly string[] FinalStates =
    [
        ExtensionCompleted,
        ExtensionFailed,
        CompensationCompleted,
        CompensationFailed,
        PaymentFailed
    ];
}

