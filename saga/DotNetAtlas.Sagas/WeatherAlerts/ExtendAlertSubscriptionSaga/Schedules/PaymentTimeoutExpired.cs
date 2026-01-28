namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Schedules;

/// <summary>
/// Schedule message indicating payment timeout has expired.
/// </summary>
public sealed record PaymentTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}

