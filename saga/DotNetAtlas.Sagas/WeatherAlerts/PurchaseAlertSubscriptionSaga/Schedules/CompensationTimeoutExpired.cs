namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Schedules;

/// <summary>
/// Message sent when the compensation timeout has expired for a saga instance.
/// Indicates that compensation (refund) did not complete within the expected timeframe.
/// </summary>
public sealed record CompensationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
