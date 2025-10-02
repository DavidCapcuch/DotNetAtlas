using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.WeatherAlerts.Common;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Application.WeatherAlerts.DisconnectCleanup;
using DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;
using DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;
using DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromCityAlerts;
using DotNetAtlas.Infrastructure.Common.Authorization;
using DotNetAtlas.Infrastructure.Common.Config;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DotNetAtlas.Api.SignalR.WeatherAlerts;

public class WeatherAlertHub : Hub<IWeatherAlertClientContract>, IWeatherAlertHubContract
{
    public const string RoutePattern = $"{InfrastructureConstants.HubsBasePath}/v1/weather-alert";

    private readonly ILogger<WeatherAlertHub> _logger;
    private readonly ICommandHandler<SubscribeForCityAlertsCommand> _subscribeForCityAlertsHandler;
    private readonly ICommandHandler<UnsubscribeFromCityAlertsCommand> _unsubscribeFromCityAlertsHandler;
    private readonly ICommandHandler<SendWeatherAlertCommand> _sendWeatherAlertHandler;
    private readonly ICommandHandler<ConnectionDisconnectCleanupCommand> _connectionDisconnectCleanupHandler;

    public WeatherAlertHub(
        ILogger<WeatherAlertHub> logger,
        ICommandHandler<SubscribeForCityAlertsCommand> subscribeForCityAlertsHandler,
        ICommandHandler<UnsubscribeFromCityAlertsCommand> unsubscribeFromCityAlertsHandler,
        ICommandHandler<SendWeatherAlertCommand> sendWeatherAlertHandler,
        ICommandHandler<ConnectionDisconnectCleanupCommand> connectionDisconnectCleanupHandler)
    {
        _logger = logger;
        _subscribeForCityAlertsHandler = subscribeForCityAlertsHandler;
        _unsubscribeFromCityAlertsHandler = unsubscribeFromCityAlertsHandler;
        _sendWeatherAlertHandler = sendWeatherAlertHandler;
        _connectionDisconnectCleanupHandler = connectionDisconnectCleanupHandler;
    }

    public async Task SubscribeForCityAlerts(AlertSubscriptionDto alertSubscriptionDto)
    {
        var connectionId = Context.ConnectionId;
        var subscribeForCityAlertsCommand = new SubscribeForCityAlertsCommand
        {
            City = alertSubscriptionDto.City,
            CountryCode = alertSubscriptionDto.CountryCode,
            ConnectionId = connectionId
        };

        var subscribeResult =
            await _subscribeForCityAlertsHandler.HandleAsync(subscribeForCityAlertsCommand, Context.ConnectionAborted);
        if (subscribeResult.IsFailed)
        {
            throw new HubException(string.Join("; ", subscribeResult.Errors.Select(e => e.Message)));
        }

        var groupName = WeatherAlertGroupNames.GroupByCitySubscriptionRequest(alertSubscriptionDto);
        await Groups.AddToGroupAsync(connectionId, groupName);
        _logger.LogInformation(
            "User: {UserIdentifier} ConnectionId: {ConnectionId} subscribed to alerts for {CityGroupName}",
            Context.UserIdentifier, connectionId, groupName);
    }

    public async Task UnsubscribeFromCityAlerts(AlertSubscriptionDto alertSubscriptionDto)
    {
        var connectionId = Context.ConnectionId;
        var unsubscribeFromCityAlertsCommand = new UnsubscribeFromCityAlertsCommand
        {
            City = alertSubscriptionDto.City,
            CountryCode = alertSubscriptionDto.CountryCode,
            ConnectionId = connectionId
        };

        var unsubscribeResult =
            await _unsubscribeFromCityAlertsHandler.HandleAsync(
                unsubscribeFromCityAlertsCommand, Context.ConnectionAborted);
        if (unsubscribeResult.IsFailed)
        {
            throw new HubException(string.Join("; ", unsubscribeResult.Errors.Select(e => e.Message)));
        }

        var groupName = WeatherAlertGroupNames.GroupByCitySubscriptionRequest(alertSubscriptionDto);
        await Groups.RemoveFromGroupAsync(connectionId, groupName);
        _logger.LogInformation(
            "User: {UserIdentifier} ConnectionId: {ConnectionId} unsubscribed from alerts for {CityGroupName}",
            Context.UserIdentifier, connectionId, groupName);
    }

    [Authorize(AuthPolicies.DevOnly, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task SendWeatherAlert(
        IAsyncEnumerable<WeatherAlert> weatherAlerts)
    {
        await foreach (var weatherAlert in weatherAlerts)
        {
            _logger.LogInformation(
                "User: {UserIdentifier} ConnectionId: {ConnectionId} sent WeatherAlert for {City}:{CountryCode}",
                Context.UserIdentifier, Context.ConnectionId, weatherAlert.City, weatherAlert.CountryCode);
            var sendWeatherAlertCommand = new SendWeatherAlertCommand
            {
                City = weatherAlert.City,
                CountryCode = weatherAlert.CountryCode,
                Message = weatherAlert.AlertMessage
            };

            var sendWeatherResult =
                await _sendWeatherAlertHandler.HandleAsync(sendWeatherAlertCommand, Context.ConnectionAborted);
            if (sendWeatherResult.IsFailed)
            {
                throw new HubException(string.Join("; ", sendWeatherResult.Errors.Select(e => e.Message)));
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation(
            "User: {UserIdentifier} ConnectionId: {ConnectionId} disconnected",
            Context.UserIdentifier, connectionId);

        var connectionDisconnectCleanupCommand = new ConnectionDisconnectCleanupCommand(connectionId);
        await _connectionDisconnectCleanupHandler
            .HandleAsync(connectionDisconnectCleanupCommand, Context.ConnectionAborted);

        await base.OnDisconnectedAsync(exception);
    }
}
