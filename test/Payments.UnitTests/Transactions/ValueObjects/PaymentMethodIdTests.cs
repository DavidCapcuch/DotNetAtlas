using System.Reflection;
using FluentResults.Extensions.FluentAssertions;
using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Pii;

namespace Payments.UnitTests.Transactions.ValueObjects;

public class PaymentMethodIdTests
{
    [Theory]
    [InlineData("t")]
    [InlineData("tok_visa_4242")]
    public void Create_WhenValid_ReturnsOk(string value)
    {
        var result = PaymentMethodId.Create(value);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be(value);
            result.Value.ToString().Should().Be(value);
        }
    }

    [Fact]
    public void Create_AtMaxLength_ReturnsOk()
    {
        var value = new string('x', 64);

        var result = PaymentMethodId.Create(value);

        result.Should().BeSuccess();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Create_WhenNullOrWhitespace_ReturnsInvalidPaymentMethod(string? value)
    {
        var result = PaymentMethodId.Create(value!);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }

    [Fact]
    public void Create_AboveMaxLength_ReturnsInvalidPaymentMethod()
    {
        var value = new string('x', 65);

        var result = PaymentMethodId.Create(value);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }

    [Fact]
    public void Type_IsMarkedAsPii()
    {
        var piiAttribute = typeof(PaymentMethodId).GetCustomAttribute<PiiAttribute>();

        piiAttribute.Should().NotBeNull("PaymentMethodId carries tokenised payment instrument data (ADR-0011)");
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var first = PaymentMethodId.Create("tok_visa_4242").Value;
        var second = PaymentMethodId.Create("tok_visa_4242").Value;

        first.Should().Be(second);
    }
}
