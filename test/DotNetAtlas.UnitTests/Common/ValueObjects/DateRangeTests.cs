using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.Common.ValueObjects;

public class DateRangeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(14)]
    public void Create_WithNumberOfDays_WhenValidDays_ReturnsSuccess(int numberOfDays)
    {
        // Arrange
        var startDate = new DateOnly(2024, 1, 1);

        // Act
        var dateRangeResult = DateRange.Create(startDate, numberOfDays);

        // Assert
        using (new AssertionScope())
        {
            dateRangeResult.Should().BeSuccess();
            dateRangeResult.Value.StartDateOnly.Should().Be(startDate);
            dateRangeResult.Value.EndDateOnly.Should().Be(startDate.AddDays(numberOfDays - 1));
            dateRangeResult.Value.LengthInDays.Should().Be(numberOfDays);
        }
    }

    [Fact]
    public void Create_WithNumberOfDays_WhenSingleDay_ReturnsRangeWithSameStartAndEnd()
    {
        // Arrange
        var startDate = new DateOnly(2024, 6, 15);

        // Act
        var dateRangeResult = DateRange.Create(startDate, 1);

        // Assert
        using (new AssertionScope())
        {
            dateRangeResult.Should().BeSuccess();
            dateRangeResult.Value.StartDateOnly.Should().Be(startDate);
            dateRangeResult.Value.EndDateOnly.Should().Be(startDate);
            dateRangeResult.Value.LengthInDays.Should().Be(1);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void Create_WithNumberOfDays_WhenInvalidDays_ReturnsValidationError(int numberOfDays)
    {
        // Arrange
        var startDate = new DateOnly(2024, 1, 1);

        // Act
        var dateRangeResult = DateRange.Create(startDate, numberOfDays);

        // Assert
        using (new AssertionScope())
        {
            dateRangeResult.Should().BeFailure();
            var validationError = dateRangeResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("DateRange.InvalidDaysCount");
        }
    }

    [Fact]
    public void Create_WithStartAndEnd_WhenValidRange_ReturnsSuccess()
    {
        // Arrange
        var startDate = new DateOnly(2024, 1, 1);
        var endDate = new DateOnly(2024, 1, 10);

        // Act
        var dateRangeResult = DateRange.Create(startDate, endDate);

        // Assert
        using (new AssertionScope())
        {
            dateRangeResult.Should().BeSuccess();
            dateRangeResult.Value.StartDateOnly.Should().Be(startDate);
            dateRangeResult.Value.EndDateOnly.Should().Be(endDate);
            dateRangeResult.Value.LengthInDays.Should().Be(10);
        }
    }

    [Fact]
    public void Create_WithStartAndEnd_WhenSameDate_ReturnsSuccess()
    {
        // Arrange
        var date = new DateOnly(2024, 6, 15);

        // Act
        var dateRangeResult = DateRange.Create(date, date);

        // Assert
        using (new AssertionScope())
        {
            dateRangeResult.Should().BeSuccess();
            dateRangeResult.Value.StartDateOnly.Should().Be(date);
            dateRangeResult.Value.EndDateOnly.Should().Be(date);
            dateRangeResult.Value.LengthInDays.Should().Be(1);
        }
    }

    [Fact]
    public void Create_WithStartAndEnd_WhenEndBeforeStart_ReturnsValidationError()
    {
        // Arrange
        var startDate = new DateOnly(2024, 6, 15);
        var endDate = new DateOnly(2024, 6, 10);

        // Act
        var dateRangeResult = DateRange.Create(startDate, endDate);

        // Assert
        using (new AssertionScope())
        {
            dateRangeResult.Should().BeFailure();
            var validationError = dateRangeResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("DateRange.InvalidDateRange");
        }
    }
}
