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
        // Arrange & Act
        var result = PaymentMethodId.Create(value);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be(value);
            result.Value.ToString().Should().Be(value);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_AtMaxLength_ReturnsOk()
    {
        // Arrange
        var value = new string('x', 64);

        // Act
        var result = PaymentMethodId.Create(value);

        // Assert
        result.Should().BeSuccess();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Create_WhenNullOrWhitespace_ReturnsInvalidPaymentMethod(string? value)
    {
        // Arrange & Act
        var result = PaymentMethodId.Create(value!);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_AboveMaxLength_ReturnsInvalidPaymentMethod()
    {
        // Arrange
        var value = new string('x', 65);

        // Act
        var result = PaymentMethodId.Create(value);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("Payments.InvalidPaymentMethod");
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public void Type_IsMarkedAsPii()
    {
        // Arrange & Act
        var piiAttribute = typeof(PaymentMethodId).GetCustomAttribute<PiiAttribute>();

        // Assert
        piiAttribute.Should().NotBeNull("PaymentMethodId carries tokenised payment instrument data (ADR-0011)");
    }
}
