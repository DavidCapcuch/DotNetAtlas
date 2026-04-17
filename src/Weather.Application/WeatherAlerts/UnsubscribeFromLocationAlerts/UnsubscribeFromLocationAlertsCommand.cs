using Platform.CQRS;
using Weather.Domain.Common.ValueObjects;

namespace Weather.Application.WeatherAlerts.UnsubscribeFromLocationAlerts;

/// <summary>
/// Command to unsubscribe from weather alerts for a specific location.
/// Handles two subscription types that can exist independently:
/// <list type="bullet">
/// <item><b>Real-time subscription</b>: Always removed. The connection is removed from the
/// SignalR group and will no longer receive real-time alerts.</item>
/// <item><b>Persisted subscription</b>: Removed only when <see cref="UserId"/> is provided.
/// Removes the database record and stops email alert notifications.</item>
/// </list>
/// </summary>
public class UnsubscribeFromLocationAlertsCommand : ICommand
{
    /// <summary>
    /// Name of the city to unsubscribe from alerts for.
    /// </summary>
    public required string City { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country code for the location.
    /// </summary>
    public required CountryCode CountryCode { get; set; }

    /// <summary>
    /// SignalR connection ID to remove from real-time alert delivery.
    /// This connection will be removed from the location's SignalR group.
    /// Required for all unsubscriptions.
    /// </summary>
    public required string ConnectionId { get; set; }

    /// <summary>
    /// Optional user ID for persisted subscription removal.
    /// When provided, the persisted subscription is removed from the database
    /// and the user will no longer receive email alerts.
    /// When null, only the real-time subscription is removed.
    /// </summary>
    public Guid? UserId { get; set; }
}
