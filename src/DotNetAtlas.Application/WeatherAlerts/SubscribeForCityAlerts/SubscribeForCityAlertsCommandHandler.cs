using System.Diagnostics;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.Forecast.Services.Requests;
using DotNetAtlas.Application.WeatherAlerts.Common;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;

public class SubscribeForCityAlertsCommandHandler : ICommandHandler<SubscribeForCityAlertsCommand>
{
    private readonly ILogger<SubscribeForCityAlertsCommandHandler> _logger;
    private readonly IGroupManager _groupManager;
    private readonly IWeatherAlertJobScheduler _jobScheduler;
    private readonly IGeocodingService _geocodingService;

    public SubscribeForCityAlertsCommandHandler(
        IGroupManager groupManager,
        IWeatherAlertJobScheduler jobScheduler,
        IGeocodingService geocodingService,
        ILogger<SubscribeForCityAlertsCommandHandler> logger)
    {
        _groupManager = groupManager;
        _jobScheduler = jobScheduler;
        _geocodingService = geocodingService;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(SubscribeForCityAlertsCommand command, CancellationToken ct)
    {
        Activity.Current?.SetTag(DiagnosticNames.City, command.City);
        Activity.Current?.SetTag(DiagnosticNames.CountryCode, command.CountryCode.ToString());

        var alertSubscriptionDto = new AlertSubscriptionDto(command.City, command.CountryCode);

        var geoResult = await _geocodingService.GetCoordinatesAsync(
            new GeocodingRequest(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode), ct);
        if (geoResult.IsFailed)
        {
            return Result.Fail(geoResult.Errors);
        }

        var groupName = WeatherAlertGroupNames.GroupByCitySubscriptionRequest(alertSubscriptionDto);
        var groupInfo =
            await _groupManager.AddConnectionIdToGroup(groupName, command.ConnectionId);
        if (groupInfo.MemberCount == 1)
        {
            _jobScheduler.ScheduleAlertJobForGroup(alertSubscriptionDto, groupName);
            _jobScheduler.TriggerAlertJobForGroup(alertSubscriptionDto, groupName);
        }

        return Result.Ok();
    }
}
