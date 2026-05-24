using Payments.Application.Transactions;
using Payments.UnitTests.Transactions;

namespace Payments.UnitTests.Application;

/// <summary>
/// Pins the ADR-0011 masking rule applied to <see cref="GetPaymentByIdResponse"/> on the way out
/// of the application layer. The DB still holds the full value; only the HTTP response is masked.
/// </summary>
public class PaymentTransactionResponseMapperTests
{
    [Fact]
    public void ToResponse_MasksPaymentMethodId_ToLastFour()
    {
        var tx = PaymentTransactionFactory.Authorized(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));

        var response = tx.ToResponse();

        // Default factory uses "tok_visa_4242" → "****4242".
        response.PaymentMethodId.Should().Be("****4242");
    }

    [Fact]
    public void ToResponse_MasksGatewayTransactionId_WhenPresent()
    {
        var tx = PaymentTransactionFactory.Authorized(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));

        var response = tx.ToResponse();

        // Default factory uses "gw-tx-abc123" → "****c123".
        response.GatewayTransactionId.Should().Be("****c123");
    }

    [Fact]
    public void ToResponse_KeepsGatewayTransactionIdNull_WhenSourceIsNull()
    {
        // Requested state never sets a GatewayTransactionId.
        var tx = PaymentTransactionFactory.Requested(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));

        var response = tx.ToResponse();

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
        PaymentTransactionResponseMapper.MaskTrailing(input).Should().Be(expected);
    }
}
