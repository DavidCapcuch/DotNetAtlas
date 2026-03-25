using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Application.WeatherAlerts.Common.Abstractions;

/// <summary>
/// Abstraction for SignalR hub operations including group management and alert notifications.
/// </summary>
/// <remarks>
/// This interface is implemented in the API layer (Infrastructure) to bridge the application
/// layer command handlers with the SignalR hub context, allowing the command handlers to
/// orchestrate group membership without directly depending on SignalR.
/// </remarks>
public interface IWeatherAlertBroadcaster
{
    /// <summary>
    /// Adds a connection to the specified alert group for receiving real-time notifications.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID to add to the group.</param>
    /// <param name="alertGroup">The alert group to join.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddConnectionToGroupAsync(string connectionId, AlertGroup alertGroup, CancellationToken ct);

    /// <summary>
    /// Removes a connection from the specified alert group.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID to remove from the group.</param>
    /// <param name="alertGroup">The alert group to leave.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveConnectionFromGroupAsync(string connectionId, AlertGroup alertGroup, CancellationToken ct);

    /// <summary>
    /// Sends a weather alert to all connections in the specified alert group.
    /// </summary>
    /// <param name="alertGroup">The alert group to notify.</param>
    /// <param name="weatherAlert">The weather alert to send.</param>
    Task BroadcastToGroupAsync(AlertGroup alertGroup, WeatherAlert weatherAlert);
}
