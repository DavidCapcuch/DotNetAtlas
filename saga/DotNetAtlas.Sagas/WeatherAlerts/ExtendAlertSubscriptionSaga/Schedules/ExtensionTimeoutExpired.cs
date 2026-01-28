namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Schedules;

/// <summary>
/// Schedule message indicating extension timeout has expired.
/// </summary>
public sealed record ExtensionTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}

