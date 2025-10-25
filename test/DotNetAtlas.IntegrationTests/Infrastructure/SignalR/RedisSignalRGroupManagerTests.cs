using DotNetAtlas.Infrastructure.SignalR;
using DotNetAtlas.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.IntegrationTests.Infrastructure.SignalR;

[Collection<SignalRTestCollection>]
public class RedisSignalRGroupManagerTests : BaseIntegrationTest
{
    private readonly RedisSignalRGroupManager _signalRGroupManager;

    public RedisSignalRGroupManagerTests(IntegrationTestFixture app)
        : base(app)
    {
        _signalRGroupManager = Scope.ServiceProvider.GetRequiredService<RedisSignalRGroupManager>();
    }

    [Fact]
    public async Task AddAndRemove_ShouldTrackCountsAndMembership()
    {
        // Arrange
        const string group = "city:Prague:CZ";
        const string conn1 = "conn-1";
        const string conn2 = "conn-2";

        // Act
        var infoAfterAddConn1 = await _signalRGroupManager.AddConnectionIdToGroup(group, conn1);
        var infoAfterAddConn2 = await _signalRGroupManager.AddConnectionIdToGroup(group, conn2);
        var groupsForConn1 = await _signalRGroupManager.GetGroupsByConnectionIdAsync(conn1);
        var infoAfterRemoveConn1 = await _signalRGroupManager.RemoveConnectionFromGroup(group, conn1);
        var groupsAfterRemoveConn1 = await _signalRGroupManager.GetGroupsByConnectionIdAsync(conn1);

        // Assert
        using (new AssertionScope())
        {
            infoAfterAddConn1.MemberCount.Should().Be(1);
            infoAfterAddConn2.MemberCount.Should().Be(2);
            groupsForConn1.Should().ContainSingle(g => g.GroupName == group && g.MemberCount == 2);
            infoAfterRemoveConn1.MemberCount.Should().Be(1);
            groupsAfterRemoveConn1.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AddingSameConnectionTwice_ShouldNotIncrementMemberCount()
    {
        // Arrange
        const string group = "city:Oslo:NO";
        const string connId = "dup-conn";

        // Act
        var afterFirstAdd = await _signalRGroupManager.AddConnectionIdToGroup(group, connId);
        var afterSecondAdd = await _signalRGroupManager.AddConnectionIdToGroup(group, connId);

        // Assert
        using (new AssertionScope())
        {
            afterFirstAdd.MemberCount.Should().Be(1);
            afterSecondAdd.MemberCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task RemovingNonMember_ShouldNotChangeMemberCount()
    {
        // Arrange
        const string group = "city:Paris:FR";
        const string connId = "member-1";
        var infoAfterAddFirstConnection = await _signalRGroupManager.AddConnectionIdToGroup(group, connId);

        // Act
        var infoAfterRemoveNonMember = await _signalRGroupManager.RemoveConnectionFromGroup(group, "not-in-group");

        // Assert
        using (new AssertionScope())
        {
            infoAfterRemoveNonMember.MemberCount.Should().Be(1);
            infoAfterAddFirstConnection.Should().Be(infoAfterRemoveNonMember);
        }
    }

    [Fact]
    public async Task GetGroupsByConnectionId_ShouldReturnEmptyForUnknownConnection()
    {
        // Arrange
        const string group = "city:Paris:FR";
        const string connId = "member-1";
        await _signalRGroupManager.AddConnectionIdToGroup(group, connId);

        // Act
        var groups = await _signalRGroupManager.GetGroupsByConnectionIdAsync("unknown-conn");

        // Assert
        groups.Should().BeEmpty();
    }
}
