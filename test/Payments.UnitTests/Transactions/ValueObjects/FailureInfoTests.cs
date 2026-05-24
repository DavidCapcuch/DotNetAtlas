using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Transactions.ValueObjects;

public class FailureInfoTests
{
    [Fact]
    public void Equality_IsByValue()
    {
        var recordedAt = DateTimeOffset.UtcNow;
        var first = FailureInfo.Create(FailureReason.InsufficientFunds, "insufficient_funds", recordedAt);
        var second = FailureInfo.Create(FailureReason.InsufficientFunds, "insufficient_funds", recordedAt);

        first.Should().Be(second);
    }

    [Fact]
    public void Inequality_WhenReasonDiffers()
    {
        var recordedAt = DateTimeOffset.UtcNow;
        var first = FailureInfo.Create(FailureReason.InsufficientFunds, "insufficient_funds", recordedAt);
        var second = FailureInfo.Create(FailureReason.FraudSuspected, "insufficient_funds", recordedAt);

        first.Should().NotBe(second);
    }

    [Fact]
    public void NullGatewayCode_IsAccepted()
    {
        var info = FailureInfo.Create(FailureReason.Unknown, gatewayCode: null, DateTimeOffset.UtcNow);

        using (new AssertionScope())
        {
            info.Reason.Should().Be(FailureReason.Unknown);
            info.GatewayCode.Should().BeNull();
        }
    }
}
