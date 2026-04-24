using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Transactions.ValueObjects;

public class FailureInfoTests
{
    [Fact]
    public void Equality_IsByValue()
    {
        var recordedAt = DateTimeOffset.UtcNow;
        var first = new FailureInfo(FailureReason.InsufficientFunds, "insufficient_funds", recordedAt);
        var second = new FailureInfo(FailureReason.InsufficientFunds, "insufficient_funds", recordedAt);

        first.Should().Be(second);
    }

    [Fact]
    public void Inequality_WhenReasonDiffers()
    {
        var recordedAt = DateTimeOffset.UtcNow;
        var first = new FailureInfo(FailureReason.InsufficientFunds, "insufficient_funds", recordedAt);
        var second = new FailureInfo(FailureReason.FraudSuspected, "insufficient_funds", recordedAt);

        first.Should().NotBe(second);
    }

    [Fact]
    public void NullGatewayCode_IsAccepted()
    {
        var info = new FailureInfo(FailureReason.Unknown, GatewayCode: null, DateTimeOffset.UtcNow);

        using (new AssertionScope())
        {
            info.Reason.Should().Be(FailureReason.Unknown);
            info.GatewayCode.Should().BeNull();
        }
    }
}
