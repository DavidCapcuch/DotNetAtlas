using FluentValidation.TestHelper;
using Weather.Application.WeatherAlerts.ExtendSubscription;

namespace Weather.UnitTests.WeatherAlerts.Validators;

public class ExtendSubscriptionCommandValidatorTests
{
    private readonly ExtendSubscriptionCommandValidator _extendSubscriptionCommandValidator = new();

    [Fact]
    public void WhenValidCommand_ShouldPassValidation()
    {
        // Arrange
        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationExtendedDays = 30,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var extendSubscriptionCommandValidationResult =
            _extendSubscriptionCommandValidator.TestValidate(extendSubscriptionCommand);

        // Assert
        extendSubscriptionCommandValidationResult.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void WhenEmptyUserId_ShouldFail()
    {
        // Arrange
        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.Empty,
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationExtendedDays = 30,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var extendSubscriptionCommandValidationResult =
            _extendSubscriptionCommandValidator.TestValidate(extendSubscriptionCommand);

        // Assert
        extendSubscriptionCommandValidationResult.ShouldHaveValidationErrorFor(c => c.UserId);
    }

    [Fact]
    public void WhenEmptyPaymentTransactionId_ShouldFail()
    {
        // Arrange
        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.Empty,
            DurationExtendedDays = 30,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var extendSubscriptionCommandValidationResult =
            _extendSubscriptionCommandValidator.TestValidate(extendSubscriptionCommand);

        // Assert
        extendSubscriptionCommandValidationResult.ShouldHaveValidationErrorFor(c => c.PaymentTransactionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void WhenInvalidDurationDays_ShouldFail(int extensionDurationDays)
    {
        // Arrange
        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationExtendedDays = extensionDurationDays,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var extendSubscriptionCommandValidationResult =
            _extendSubscriptionCommandValidator.TestValidate(extendSubscriptionCommand);

        // Assert
        extendSubscriptionCommandValidationResult.ShouldHaveValidationErrorFor(c => c.DurationExtendedDays);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    public void WhenValidDurationDays_ShouldPass(int extensionDurationDays)
    {
        // Arrange
        var extendSubscriptionCommand = new ExtendSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            DurationExtendedDays = extensionDurationDays,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var extendSubscriptionCommandValidationResult =
            _extendSubscriptionCommandValidator.TestValidate(extendSubscriptionCommand);

        // Assert
        extendSubscriptionCommandValidationResult.ShouldNotHaveAnyValidationErrors();
    }
}
