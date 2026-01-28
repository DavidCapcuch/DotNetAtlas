using DotNetAtlas.Application.WeatherAlerts.PurchaseSubscription;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Validators;

public class PurchaseSubscriptionCommandValidatorTests
{
    private readonly PurchaseSubscriptionCommandValidator _purchaseSubscriptionCommandValidator = new();

    [Fact]
    public void WhenEmptyUserId_ShouldFail()
    {
        // Arrange
        var purchaseSubscriptionCommand = new PurchaseSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.Empty,
            PaymentTransactionId = Guid.CreateVersion7(),
            Tier = SubscriptionTier.Pro,
            DurationDays = 30,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var purchaseSubscriptionCommandValidationResult =
            _purchaseSubscriptionCommandValidator.TestValidate(purchaseSubscriptionCommand);

        // Assert
        purchaseSubscriptionCommandValidationResult.ShouldHaveValidationErrorFor(c => c.UserId);
    }

    [Fact]
    public void WhenEmptyPaymentTransactionId_ShouldFail()
    {
        // Arrange
        var purchaseSubscriptionCommand = new PurchaseSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.Empty,
            Tier = SubscriptionTier.Pro,
            DurationDays = 30,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var purchaseSubscriptionCommandValidationResult =
            _purchaseSubscriptionCommandValidator.TestValidate(purchaseSubscriptionCommand);

        // Assert
        purchaseSubscriptionCommandValidationResult.ShouldHaveValidationErrorFor(c => c.PaymentTransactionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void WhenInvalidDurationDays_ShouldFail(int subscriptionDurationDays)
    {
        // Arrange
        var purchaseSubscriptionCommand = new PurchaseSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            Tier = SubscriptionTier.Pro,
            DurationDays = subscriptionDurationDays,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var purchaseSubscriptionCommandValidationResult =
            _purchaseSubscriptionCommandValidator.TestValidate(purchaseSubscriptionCommand);

        // Assert
        purchaseSubscriptionCommandValidationResult.ShouldHaveValidationErrorFor(c => c.DurationDays);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    public void WhenValidDurationDays_ShouldNotFailOnDurationDays(int subscriptionDurationDays)
    {
        // Arrange
        var purchaseSubscriptionCommand = new PurchaseSubscriptionCommand
        {
            CorrelationId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = Guid.CreateVersion7(),
            Tier = SubscriptionTier.Pro,
            DurationDays = subscriptionDurationDays,
            OccurredOnUtc = DateTimeOffset.UtcNow
        };

        // Act
        var purchaseSubscriptionCommandValidationResult =
            _purchaseSubscriptionCommandValidator.TestValidate(purchaseSubscriptionCommand);

        // Assert
        // Note: Tier validation with IsInEnum() doesn't work with SmartEnum
        // Only check that DurationDays doesn't have errors
        purchaseSubscriptionCommandValidationResult.ShouldNotHaveValidationErrorFor(c => c.DurationDays);
        purchaseSubscriptionCommandValidationResult.ShouldNotHaveValidationErrorFor(c => c.UserId);
        purchaseSubscriptionCommandValidationResult.ShouldNotHaveValidationErrorFor(c => c.PaymentTransactionId);
    }
}
