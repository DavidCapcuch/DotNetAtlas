using Platform.CQRS;
using Weather.Domain.Common.ValueObjects;

namespace Weather.Application.WeatherAlerts.SubscribeForLocationAlerts;

/// <summary>
/// Command to subscribe to weather alerts for a specific location.
/// Supports two subscription types that can be used independently or together:
/// <list type="bullet">
/// <item><b>Real-time subscription</b>: Always created. The connection is added to a SignalR group
/// for immediate alert delivery. Managed in Redis, not persisted to database.</item>
/// <item><b>Persisted subscription</b>: Created only when <see cref="UserId"/> is provided.
/// Stored in the database and enables email alert notifications.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Design Note (Demonstration Purposes):</b> For simplicity, authenticated users are automatically
/// enrolled in email alerts when they subscribe to a location. In a production system, this would
/// typically be split into separate commands (e.g., <c>SubscribeForRealTimeAlertsCommand</c> and
/// <c>SubscribeForEmailAlertsCommand</c>) to allow users to opt-in/out of email notifications
/// independently from real-time alerts.
/// </para>
/// <para>
/// The current combined approach was chosen to demonstrate the full notification flow
/// (SignalR + email via transactional outbox) without requiring additional UI for email preferences.
/// </para>
/// </remarks>
public class SubscribeForLocationAlertsCommand : ICommand
{
    /// <summary>
    /// Name of the city to subscribe to alerts for.
    /// </summary>
    public required string City { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country code for the location.
    /// </summary>
    public required CountryCode CountryCode { get; set; }

    /// <summary>
    /// Connection ID for real-time alert delivery.
    /// </summary>
    public required string ConnectionId { get; set; }

    /// <summary>
    /// Optional user ID for persisted subscriptions.
    /// When provided, the subscription is persisted to the database and the user
    /// receives email alerts in addition to real-time notifications.
    /// When null, only real-time alerts are delivered for this connection.
    /// </summary>
    public Guid? UserId { get; set; }
}
