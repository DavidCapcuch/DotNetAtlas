using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Application.WeatherAlerts.SubscribeForLocationAlerts;
using DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromLocationAlerts;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.SharedKernel.Errors;
using Microsoft.AspNetCore.SignalR;

namespace DotNetAtlas.Api.SignalRHubs.WeatherAlerts;

public class WeatherAlertHub : Hub<IWeatherAlertClientContract>, IWeatherAlertHubContract
{
    public const string RoutePattern = $"{InfrastructureConstants.HubsBasePath}/v1/weather-alert";

    private readonly ILogger<WeatherAlertHub> _logger;
    private readonly ICommandHandler<SubscribeForLocationAlertsCommand> _subscribeForCityAlertsHandler;
    private readonly ICommandHandler<UnsubscribeFromLocationAlertsCommand> _unsubscribeFromCityAlertsHandler;

    public WeatherAlertHub(
        ILogger<WeatherAlertHub> logger,
        ICommandHandler<SubscribeForLocationAlertsCommand> subscribeForCityAlertsHandler,
        ICommandHandler<UnsubscribeFromLocationAlertsCommand> unsubscribeFromCityAlertsHandler)
    {
        _logger = logger;
        _subscribeForCityAlertsHandler = subscribeForCityAlertsHandler;
        _unsubscribeFromCityAlertsHandler = unsubscribeFromCityAlertsHandler;
    }

    public async Task SubscribeForLocationAlerts(AlertSubscriptionDto alertSubscriptionDto)
    {
        var connectionId = Context.ConnectionId;
        var userId = ExtractUserIdFromUserIdentifier(Context.UserIdentifier);

        var subscribeForLocationAlertsCommand = new SubscribeForLocationAlertsCommand
        {
            City = alertSubscriptionDto.City,
            CountryCode = alertSubscriptionDto.CountryCode,
            ConnectionId = connectionId,
            UserId = userId
        };

        var subscribeResult =
            await _subscribeForCityAlertsHandler.HandleAsync(subscribeForLocationAlertsCommand,
                Context.ConnectionAborted);
        if (subscribeResult.IsFailed)
        {
            throw new HubException(subscribeResult.Errors.ToErrorsSummary());
        }

        _logger.LogInformation(
            "User: {UserIdentifier} ConnectionId: {ConnectionId} subscribed to alerts for {City}:{CountryCode}",
            Context.UserIdentifier, connectionId, alertSubscriptionDto.City, alertSubscriptionDto.CountryCode);
    }

    public async Task UnsubscribeFromLocationAlerts(AlertSubscriptionDto alertSubscriptionDto)
    {
        var connectionId = Context.ConnectionId;
        var userId = ExtractUserIdFromUserIdentifier(Context.UserIdentifier);

        var unsubscribeFromLocationAlertsCommand = new UnsubscribeFromLocationAlertsCommand
        {
            City = alertSubscriptionDto.City,
            CountryCode = alertSubscriptionDto.CountryCode,
            ConnectionId = connectionId,
            UserId = userId
        };

        var unsubscribeResult =
            await _unsubscribeFromCityAlertsHandler.HandleAsync(
                unsubscribeFromLocationAlertsCommand, Context.ConnectionAborted);
        if (unsubscribeResult.IsFailed)
        {
            throw new HubException(unsubscribeResult.Errors.ToErrorsSummary());
        }

        _logger.LogInformation(
            "User: {UserIdentifier} ConnectionId: {ConnectionId} unsubscribed from alerts for {City}:{CountryCode}",
            Context.UserIdentifier, connectionId, alertSubscriptionDto.City, alertSubscriptionDto.CountryCode);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation(
            "User: {UserIdentifier} ConnectionId: {ConnectionId} disconnected",
            Context.UserIdentifier, connectionId);

        await base.OnDisconnectedAsync(exception);
    }

    private static Guid? ExtractUserIdFromUserIdentifier(string? contextUserIdentifier)
    {
        if (Guid.TryParse(contextUserIdentifier, out var parsedUserId))
        {
            return parsedUserId;
        }

        return null;
    }
}
