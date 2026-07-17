using Payments.Application.Abstractions;
using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Application;

public class GatewayResponseClassifierTests
{
    [Theory]
    [InlineData("insufficient_funds", nameof(FailureReason.InsufficientFunds))]
    [InlineData("card_declined", nameof(FailureReason.GatewayDeclined))]
    [InlineData("fraud_suspected", nameof(FailureReason.FraudSuspected))]
    [InlineData("timeout", nameof(FailureReason.GatewayTimeout))]
    [InlineData("cancelled_by_user", nameof(FailureReason.Cancelled))]
    public void Classify_MapsKnownCodesToExpectedFailureReason(string code, string expectedReasonName)
    {
        // Arrange & Act
        var actual = GatewayResponseClassifier.Classify(code);

        // Assert
        actual.Name.Should().Be(expectedReasonName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("weird_unmapped_code")]
    [InlineData("INSUFFICIENT_FUNDS")] // case-sensitive — uppercase is unknown
    public void Classify_MapsUnknownOrBlankCodeToUnknown(string? code)
    {
        // Arrange & Act
        var actual = GatewayResponseClassifier.Classify(code);

        // Assert
        actual.Should().Be(FailureReason.Unknown);
    }
}
