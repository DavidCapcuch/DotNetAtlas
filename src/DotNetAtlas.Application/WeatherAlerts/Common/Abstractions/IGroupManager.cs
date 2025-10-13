namespace DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;

public interface IGroupManager
{
    Task<GroupInfo> AddConnectionIdToGroup(string groupName, string connectionId);
    Task<GroupInfo> RemoveConnectionFromGroup(string groupName, string connectionId);
    Task<IReadOnlyList<GroupInfo>> GetGroupsByConnectionIdAsync(string connectionId);
}
