using Weather.Domain.Alerts.ValueObjects;
using Weather.Domain.Common.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.ValueObjects;

public class AlertGroupTests
{
    [Fact]
    public void From_WhenValidInput_ReturnsAlertGroupWithCorrectProperties()
    {
        // Arrange
        var city = City.Create("Prague").Value;
        const CountryCode countryCode = CountryCode.CZ;

        // Act
        var alertGroup = AlertGroup.From(city, countryCode);

        // Assert
        using (new AssertionScope())
        {
            alertGroup.Should().NotBeNull();
            alertGroup.City.Should().Be(city);
            alertGroup.CountryCode.Should().Be(countryCode);
        }
    }

    [Fact]
    public void From_WhenCreated_GeneratesUppercaseGroupName()
    {
        // Arrange
        var city = City.Create("Prague").Value;
        const CountryCode countryCode = CountryCode.CZ;

        // Act
        var alertGroup = AlertGroup.From(city, countryCode);

        // Assert
        alertGroup.GroupName.Should().Be("PRAGUE:CZ");
    }

    [Theory]
    [InlineData("New York", CountryCode.US, "NEW YORK:US")]
    [InlineData("london", CountryCode.GB, "LONDON:GB")]
    [InlineData("BERLIN", CountryCode.DE, "BERLIN:DE")]
    public void From_WhenDifferentCities_GeneratesCorrectGroupName(string cityName,
        CountryCode countryCode,
        string expectedGroupName)
    {
        // Arrange
        var city = City.Create(cityName).Value;

        // Act
        var alertGroup = AlertGroup.From(city, countryCode);

        // Assert
        alertGroup.GroupName.Should().Be(expectedGroupName);
    }

    [Fact]
    public void From_WhenSameCityDifferentCountry_GeneratesDifferentGroupNames()
    {
        // Arrange
        var cityFrance = City.Create("Paris").Value;
        var cityUs = City.Create("Paris").Value;

        // Act
        var alertGroupFrance = AlertGroup.From(cityFrance, CountryCode.FR);
        var alertGroupUs = AlertGroup.From(cityUs, CountryCode.US);

        // Assert
        using (new AssertionScope())
        {
            alertGroupFrance.GroupName.Should().Be("PARIS:FR");
            alertGroupUs.GroupName.Should().Be("PARIS:US");
            alertGroupFrance.GroupName.Should().NotBe(alertGroupUs.GroupName);
        }
    }

    [Fact]
    public void From_WhenSameInput_ReturnsSameGroupName()
    {
        // Arrange
        var city1 = City.Create("Prague").Value;
        var city2 = City.Create("Prague").Value;
        const CountryCode countryCode = CountryCode.CZ;

        // Act
        var alertGroup1 = AlertGroup.From(city1, countryCode);
        var alertGroup2 = AlertGroup.From(city2, countryCode);

        // Assert
        alertGroup1.GroupName.Should().Be(alertGroup2.GroupName);
    }

    [Fact]
    public void From_WhenCityHasMixedCase_NormalizesToUppercase()
    {
        // Arrange
        var city = City.Create("pRaGuE").Value;
        const CountryCode countryCode = CountryCode.CZ;

        // Act
        var alertGroup = AlertGroup.From(city, countryCode);

        // Assert
        alertGroup.GroupName.Should().Be("PRAGUE:CZ");
    }
}
