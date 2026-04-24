using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Transactions.ValueObjects;

public class FailureReasonTests
{
    [Fact]
    public void List_ContainsAllSixReasons()
    {
        FailureReason.List.Should().HaveCount(6)
            .And.Contain(new[]
            {
                FailureReason.GatewayDeclined,
                FailureReason.GatewayTimeout,
                FailureReason.InsufficientFunds,
                FailureReason.FraudSuspected,
                FailureReason.Cancelled,
                FailureReason.Unknown,
            });
    }

    [Theory]
    [InlineData(nameof(FailureReason.GatewayDeclined))]
    [InlineData(nameof(FailureReason.GatewayTimeout))]
    [InlineData(nameof(FailureReason.InsufficientFunds))]
    [InlineData(nameof(FailureReason.FraudSuspected))]
    [InlineData(nameof(FailureReason.Cancelled))]
    [InlineData(nameof(FailureReason.Unknown))]
    public void FromName_RoundTrips(string name)
    {
        var reason = FailureReason.FromName(name);

        reason.Name.Should().Be(name);
    }
}
