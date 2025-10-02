using System.Diagnostics;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherAlerts.Common;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromCityAlerts;

public class UnsubscribeFromCityAlertsCommandHandler : ICommandHandler<UnsubscribeFromCityAlertsCommand>
{
    private readonly ILogger<UnsubscribeFromCityAlertsCommandHandler> _logger;
    private readonly IGroupManager _groupManager;
    private readonly IWeatherAlertJobScheduler _jobScheduler;

    public UnsubscribeFromCityAlertsCommandHandler(
        IGroupManager groupManager,
        ILogger<UnsubscribeFromCityAlertsCommandHandler> logger,
        IWeatherAlertJobScheduler jobScheduler)
    {
        _groupManager = groupManager;
        _logger = logger;
        _jobScheduler = jobScheduler;
    }

    public async Task<Result> HandleAsync(UnsubscribeFromCityAlertsCommand command, CancellationToken ct)
    {
        Activity.Current?.SetTag(DiagnosticNames.City, command.City);
        Activity.Current?.SetTag(DiagnosticNames.CountryCode, command.CountryCode.ToString());

        var alertSubscriptionDto = new AlertSubscriptionDto(command.City, command.CountryCode);

        var groupName = WeatherAlertGroupNames.GroupByCitySubscriptionRequest(alertSubscriptionDto);
        var groupInfo =
            await _groupManager.RemoveConnectionFromGroup(groupName, command.ConnectionId);
        if (groupInfo.MemberCount == 0)
        {
            _jobScheduler.RemoveAlertJobForGroup(groupName);
            _logger.LogInformation("Group {Group} is empty. Unscheduled alerts", groupName);
        }

        return Result.Ok();
    }
}
