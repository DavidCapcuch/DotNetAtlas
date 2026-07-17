using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Transactions.ValueObjects;

public class FailureInfoTests
{
    // The former Equality_IsByValue / Inequality_WhenReasonDiffers pair tested compiler-synthesized
    // record equality (FailureInfo is a bare sealed record : ValueObject; the base declares no
    // equality members) — a language guarantee, not domain logic, so neither needs a dedicated test.
    // Value equality is incidentally relied on by PaymentTransactionFailureTests'
    // tx.FailureInfo.Should().Be(failureInfo) assertions.

    [Fact]
    public void Create_WhenGatewayCodeNull_AcceptsAndPreservesReason()
    {
        // Arrange
        var recordedAtUtc = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

        // Act
        var info = FailureInfo.Create(FailureReason.Unknown, gatewayCode: null, recordedAtUtc);

        // Assert
        using (new AssertionScope())
        {
            info.Reason.Should().Be(FailureReason.Unknown);
            info.GatewayCode.Should().BeNull();
            info.RecordedAtUtc.Should().Be(recordedAtUtc);
        }
    }
}
