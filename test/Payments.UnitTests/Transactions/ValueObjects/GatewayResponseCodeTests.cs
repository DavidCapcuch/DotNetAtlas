using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Transactions.ValueObjects;

public class GatewayResponseCodeTests
{
    [Fact]
    public void Equality_IsByValue()
    {
        var first = new GatewayResponseCode("ok", "Approved");
        var second = new GatewayResponseCode("ok", "Approved");

        first.Should().Be(second);
    }

    [Fact]
    public void Inequality_WhenCodeDiffers()
    {
        var first = new GatewayResponseCode("ok", "Approved");
        var second = new GatewayResponseCode("declined", "Approved");

        first.Should().NotBe(second);
    }
}
