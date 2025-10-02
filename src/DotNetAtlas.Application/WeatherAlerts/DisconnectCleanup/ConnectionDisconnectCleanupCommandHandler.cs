using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.DisconnectCleanup;

public sealed class ConnectionDisconnectCleanupHandler : ICommandHandler<ConnectionDisconnectCleanupCommand>
{
    private readonly IGroupManager _groupManager;
    private readonly IWeatherAlertJobScheduler _jobScheduler;
    private readonly ILogger<ConnectionDisconnectCleanupHandler> _logger;

    public ConnectionDisconnectCleanupHandler(
        IGroupManager groupManager,
        IWeatherAlertJobScheduler jobScheduler,
        ILogger<ConnectionDisconnectCleanupHandler> logger)
    {
        _groupManager = groupManager;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ConnectionDisconnectCleanupCommand command, CancellationToken ct)
    {
        var groupsToRemoveFrom =
            await _groupManager.GetGroupsByConnectionIdAsync(command.ConnectionId);
        var removeConnectionFromGroupTasks =
            groupsToRemoveFrom.Select(async groupInfo =>
            {
                var afterRemoveGroupInfo =
                    await _groupManager.RemoveConnectionFromGroup(groupInfo.GroupName, command.ConnectionId);
                if (afterRemoveGroupInfo.MemberCount == 0)
                {
                    _jobScheduler.RemoveAlertJobForGroup(groupInfo.GroupName);
                    _logger.LogInformation("Group {Group} is empty. Unscheduled alerts", groupInfo.GroupName);
                }
            });

        await Task.WhenAll(removeConnectionFromGroupTasks);

        return Result.Ok();
    }
}
