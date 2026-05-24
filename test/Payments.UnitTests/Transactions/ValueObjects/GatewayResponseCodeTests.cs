using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Transactions.ValueObjects;

public class GatewayResponseCodeTests
{
    [Fact]
    public void Equality_IsByValue()
    {
        var first = GatewayResponseCode.Create("ok", "Approved");
        var second = GatewayResponseCode.Create("ok", "Approved");

        first.Should().Be(second);
    }

    [Fact]
    public void Inequality_WhenCodeDiffers()
    {
        var first = GatewayResponseCode.Create("ok", "Approved");
        var second = GatewayResponseCode.Create("declined", "Approved");

        first.Should().NotBe(second);
    }
}
