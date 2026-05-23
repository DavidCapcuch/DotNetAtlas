using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Notifications.Application.Email;
using Notifications.Infrastructure.Email;
using Xunit;

namespace Notifications.UnitTests.Email;

public sealed class MockEmailGatewayTests
{
    [Fact]
    public async Task SendAsync_AlwaysReturnsOk()
    {
        var gateway = new MockEmailGateway(NullLogger<MockEmailGateway>.Instance, new FakeTimeProvider());
        var result = await gateway.SendAsync(
            new EmailMessage("user-1", "Subject", "Body"),
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
    }
}
