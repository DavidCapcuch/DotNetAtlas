namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Schedules;

/// <summary>
/// Message sent when the refund timeout has expired for a saga instance.
/// </summary>
public sealed record RefundTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}

