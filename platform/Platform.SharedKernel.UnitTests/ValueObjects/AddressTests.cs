using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Platform.SharedKernel.UnitTests.ValueObjects;

public class AddressTests
{
    [Fact]
    public void Create_WhenAllFieldsValid_TrimsAndUppercasesCountry()
    {
        // Arrange & Act
        var result = Address.Create(
            street1: "  1 Wenceslas Sq.  ",
            street2: "  Flat 3  ",
            city: "  Prague  ",
            state: "  Hlavní město  ",
            postalCode: "  110 00  ",
            countryCode: "cz");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var addr = result.Value;
            addr.Street1.Should().Be("1 Wenceslas Sq.");
            addr.Street2.Should().Be("Flat 3");
            addr.City.Should().Be("Prague");
            addr.State.Should().Be("Hlavní město");
            addr.PostalCode.Should().Be("110 00");
            addr.CountryCode.Should().Be("CZ");
        }
    }

    [Fact]
    public void Create_WithOptionalFieldsOmitted_ReturnsSuccess()
    {
        // Arrange & Act
        var result = Address.Create("Main St 1", street2: null, "Prague", state: null, "110 00", "CZ");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Street2.Should().BeNull();
            result.Value.State.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenStreet1MissingOrBlank_ReturnsInvalidStreet1(string? street1)
    {
        // Arrange & Act
        var result = Address.Create(street1!, null, "Prague", null, "110 00", "CZ");

        // Assert
        AssertValidationError(result, "Address.InvalidStreet1");
    }

    [Fact]
    public void Create_WhenStreet1ExceedsMaxLength_ReturnsInvalidStreet1()
    {
        // Arrange & Act
        var result = Address.Create(new string('a', 201), null, "Prague", null, "110 00", "CZ");

        // Assert
        AssertValidationError(result, "Address.InvalidStreet1");
    }

    [Fact]
    public void Create_WhenStreet1AtMaxLength_ReturnsSuccess()
    {
        // Arrange & Act
        var result = Address.Create(new string('a', 200), null, "Prague", null, "110 00", "CZ");

        // Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void Create_WhenStreet2ExceedsMaxLength_ReturnsInvalidStreet2()
    {
        // Arrange & Act
        var result = Address.Create("Main St 1", new string('a', 201), "Prague", null, "110 00", "CZ");

        // Assert
        AssertValidationError(result, "Address.InvalidStreet2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenCityMissingOrBlank_ReturnsInvalidCity(string? city)
    {
        // Arrange & Act
        var result = Address.Create("Main St 1", null, city!, null, "110 00", "CZ");

        // Assert
        AssertValidationError(result, "Address.InvalidCity");
    }

    [Fact]
    public void Create_WhenCityExceedsMaxLength_ReturnsInvalidCity()
    {
        // Arrange & Act
        var result = Address.Create("Main St 1", null, new string('a', 101), null, "110 00", "CZ");

        // Assert
        AssertValidationError(result, "Address.InvalidCity");
    }

    [Fact]
    public void Create_WhenStateExceedsMaxLength_ReturnsInvalidState()
    {
        // Arrange & Act
        var result = Address.Create("Main St 1", null, "Prague", new string('a', 101), "110 00", "CZ");

        // Assert
        AssertValidationError(result, "Address.InvalidState");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenPostalCodeMissingOrBlank_ReturnsInvalidPostalCode(string? postalCode)
    {
        // Arrange & Act
        var result = Address.Create("Main St 1", null, "Prague", null, postalCode!, "CZ");

        // Assert
        AssertValidationError(result, "Address.InvalidPostalCode");
    }

    [Fact]
    public void Create_WhenPostalCodeExceedsMaxLength_ReturnsInvalidPostalCode()
    {
        // Arrange & Act
        var result = Address.Create("Main St 1", null, "Prague", null, new string('a', 21), "CZ");

        // Assert
        AssertValidationError(result, "Address.InvalidPostalCode");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("CZE")]
    public void Create_WhenCountryCodeHasInvalidLength_ReturnsInvalidCountryCode(string? countryCode)
    {
        // Arrange & Act
        var result = Address.Create("Main St 1", null, "Prague", null, "110 00", countryCode!);

        // Assert
        AssertValidationError(result, "Address.InvalidCountryCode");
    }

    private static void AssertValidationError<T>(FluentResults.Result<T> result, string expectedErrorCode)
    {
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var error = result.Errors[0] as ValidationError;
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be(expectedErrorCode);
        }
    }
}
