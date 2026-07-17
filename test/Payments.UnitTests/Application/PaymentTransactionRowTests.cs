using Payments.Application.Transactions;
using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Application;

/// <summary>
/// Pins the ADR-0011 masking rule applied by <see cref="PaymentTransactionRow.ToResponse"/> on the
/// way out of the application layer. The DB (and the projected row) still hold the full value; only
/// the HTTP response is masked.
/// </summary>
[Trait("Category", "security")]
public class PaymentTransactionRowTests
{
    [Fact]
    public void ToResponse_MasksPaymentMethodId_ToLastFour()
    {
        // Arrange & Act
        var response = Row(paymentMethodId: "tok_visa_4242").ToResponse();

        // Assert
        response.PaymentMethodId.Should().Be("****4242");
    }

    [Fact]
    public void ToResponse_MasksGatewayTransactionId_WhenPresent()
    {
        // Arrange & Act
        var response = Row(gatewayTransactionId: "gw-tx-abc123").ToResponse();

        // Assert
        response.GatewayTransactionId.Should().Be("****c123");
    }

    [Fact]
    public void ToResponse_KeepsGatewayTransactionIdNull_WhenSourceIsNull()
    {
        // Arrange & Act
        var response = Row(gatewayTransactionId: null).ToResponse();

        // Assert
        response.GatewayTransactionId.Should().BeNull();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a", "***")]
    [InlineData("ab", "***")]
    [InlineData("abc", "***")]
    [InlineData("abcd", "***")]
    [InlineData("abcde", "****bcde")]
    [InlineData("tok_visa_4242", "****4242")]
    [InlineData("gw-tx-abc123", "****c123")]
    public void MaskTrailing_LongInputs_ReturnLastFourPrefixedByFourStars(string input, string expected)
    {
        // Arrange & Act & Assert
        PaymentTransactionRow.MaskTrailing(input).Should().Be(expected);
    }

    private static PaymentTransactionRow Row(
        string paymentMethodId = "tok_visa_4242",
        string? gatewayTransactionId = "gw-tx-abc123") =>
        new()
        {
            PaymentId = Guid.Empty,
            BuyerId = Guid.Empty,
            OrderId = Guid.Empty,
            Amount = 100m,
            Currency = "USD",
            PaymentMethodId = paymentMethodId,
            Status = PaymentStatus.Authorized,
            GatewayTransactionId = gatewayTransactionId,
        };
}
