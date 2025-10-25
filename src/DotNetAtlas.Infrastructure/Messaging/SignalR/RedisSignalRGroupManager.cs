using System.Diagnostics;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherAlerts.Common;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DotNetAtlas.Infrastructure.Messaging.SignalR;

public sealed class RedisSignalRGroupManager : IGroupManager
{
    private static string ConnectionGroupsKey(string connectionId) => $"signalr:conn:{connectionId}:groups";
    private static string GroupCountKey(string groupName) => $"signalr:group:{groupName}:count";

    private readonly ILogger<RedisSignalRGroupManager> _logger;
    private readonly IDatabase _redisDb;
    private readonly IDotNetAtlasInstrumentation _dotNetAtlasInstrumentation;

    public RedisSignalRGroupManager(
        ILogger<RedisSignalRGroupManager> logger,
        IConnectionMultiplexer redis,
        IDotNetAtlasInstrumentation dotNetAtlasInstrumentation)
    {
        _logger = logger;
        _dotNetAtlasInstrumentation = dotNetAtlasInstrumentation;
        _redisDb = redis.GetDatabase();
    }

    public async Task<GroupInfo> AddConnectionIdToGroup(string groupName, string connectionId)
    {
        using var activity = _dotNetAtlasInstrumentation.ActivitySource
            .StartActivity(ActivityKind.Internal,
                tags: [new KeyValuePair<string, object?>(DiagnosticNames.SignalRGroup, groupName)]);
        var connectionGroupsKey = ConnectionGroupsKey(connectionId);
        var groupCountKey = GroupCountKey(groupName);
        var addToGroupMemberCountScriptResult = await _redisDb.ScriptEvaluateAsync(
            """
            local connectionGroupsKey = KEYS[1]
            local groupCountKey = KEYS[2]
            local groupNameToRemove = ARGV[1]

            local wasAdded = redis.call('SADD', connectionGroupsKey, groupNameToRemove) == 1
            if wasAdded then
              return redis.call('INCR', groupCountKey)
            end

            local currentMemberCount = redis.call('GET', groupCountKey)
            return tonumber(currentMemberCount) or 0
            """, keys:
            [
                connectionGroupsKey,
                groupCountKey
            ], values: [groupName]);

        var newMemberCount = (int)addToGroupMemberCountScriptResult;
        _logger.LogDebug("Group {Group} member count is now {Count}", groupName, newMemberCount);

        return new GroupInfo(groupName, newMemberCount);
    }

    public async Task<GroupInfo> RemoveConnectionFromGroup(string groupName, string connectionId)
    {
        using var activity = _dotNetAtlasInstrumentation.ActivitySource
            .StartActivity(ActivityKind.Internal,
                tags: [new KeyValuePair<string, object?>(DiagnosticNames.SignalRGroup, groupName)]);
        var memberCountScriptResult = await _redisDb.ScriptEvaluateAsync(
            """
            local connectionGroupsKey = KEYS[1]
            local groupCountKey = KEYS[2]
            local groupNameToRemove = ARGV[1]

            local wasRemoved = redis.call('SREM', connectionGroupsKey, groupNameToRemove) == 1
            local currentCount = tonumber(redis.call('GET', groupCountKey)) or 0

            if not wasRemoved then
              return currentCount
            end

            if currentCount <= 1 then
              redis.call('DEL', groupCountKey)
              return 0
            end

            local newCount = tonumber(redis.call('DECR', groupCountKey)) or 0
            if newCount < 0 then
              redis.call('DEL', groupCountKey)
              return 0
            end
            return newCount
            """, keys:
            [
                ConnectionGroupsKey(connectionId),
                GroupCountKey(groupName)
            ], values: [groupName]);
        var currentMemberCount = (int)memberCountScriptResult;

        _logger.LogDebug("Group {Group} member count is now {MemberCount}", groupName, currentMemberCount);
        return new GroupInfo(groupName, currentMemberCount);
    }

    public async Task<IReadOnlyList<GroupInfo>> GetGroupsByConnectionIdAsync(string connectionId)
    {
        var groupNames = await _redisDb.SetMembersAsync(ConnectionGroupsKey(connectionId));

        if (groupNames.Length == 0)
        {
            return [];
        }

        var groupCountKeys = groupNames
            .Select(g => new RedisKey(GroupCountKey(g!)))
            .ToArray();

        var groupCountValues = await _redisDb.StringGetAsync(groupCountKeys);

        var groupInfos = new GroupInfo[groupNames.Length];
        for (var i = 0; i < groupNames.Length; i++)
        {
            var groupCountValue = groupCountValues[i];
            var memberCount = 0;
            if (groupCountValue.HasValue && int.TryParse(groupCountValue.ToString(), out var parsed))
            {
                memberCount = parsed < 0 ? 0 : parsed;
            }

            groupInfos[i] = new GroupInfo(groupNames[i]!, memberCount);
        }

        return groupInfos;
    }
}
