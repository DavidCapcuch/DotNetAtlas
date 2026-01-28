using DotNetAtlas.Domain.Feedback;
using DotNetAtlas.Domain.Feedback.Events;
using DotNetAtlas.Domain.Feedback.ValueObjects;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.WeatherFeedback.Aggregates;

public class FeedbackTests
{
    [Fact]
    public void Create_WhenValidInput_ReturnsSuccess()
    {
        // Arrange
        var feedbackText = FeedbackText.Create("Great weather app!").Value;
        var rating = FeedbackRating.Create(5).Value;
        var userId = Guid.CreateVersion7();

        // Act
        var feedbackResult = Feedback.Create(feedbackText, rating, userId);

        // Assert
        using (new AssertionScope())
        {
            feedbackResult.Should().BeSuccess();
            feedbackResult.Value.FeedbackText.Should().Be(feedbackText);
            feedbackResult.Value.Rating.Should().Be(rating);
            feedbackResult.Value.CreatedByUser.Should().Be(userId);
            feedbackResult.Value.Id.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void Create_WhenCalled_RaisesFeedbackCreatedDomainEvent()
    {
        // Arrange
        var feedbackText = FeedbackText.Create("Excellent service!").Value;
        var rating = FeedbackRating.Create(4).Value;
        var userId = Guid.CreateVersion7();

        // Act
        var feedbackResult = Feedback.Create(feedbackText, rating, userId);

        // Assert
        var domainEvents = feedbackResult.Value.PopDomainEvents();

        using (new AssertionScope())
        {
            feedbackResult.Should().BeSuccess();
            domainEvents.Should().ContainSingle();
            var domainEvent = domainEvents[0] as FeedbackCreatedDomainEvent;
            domainEvent.Should().NotBeNull();
            domainEvent.FeedbackId.Should().Be(feedbackResult.Value.Id);
            domainEvent.UserId.Should().Be(userId);
            domainEvent.Rating.Should().Be(rating);
            domainEvent.Text.Should().Be(feedbackText);
        }
    }

    [Fact]
    public void ChangeFeedback_WhenSameUser_UpdatesFeedbackAndReturnsSuccess()
    {
        // Arrange
        var originalText = FeedbackText.Create("Original feedback").Value;
        var originalRating = FeedbackRating.Create(3).Value;
        var userId = Guid.CreateVersion7();
        var feedback = Feedback.Create(originalText, originalRating, userId).Value;
        _ = feedback.PopDomainEvents();

        var newText = FeedbackText.Create("Updated feedback").Value;
        var newRating = FeedbackRating.Create(5).Value;

        // Act
        var changeResult = feedback.ChangeFeedback(newText, newRating, userId);

        // Assert
        using (new AssertionScope())
        {
            changeResult.Should().BeSuccess();
            feedback.FeedbackText.Should().Be(newText);
            feedback.Rating.Should().Be(newRating);
        }
    }

    [Fact]
    public void ChangeFeedback_WhenDifferentUser_ReturnsForbiddenError()
    {
        // Arrange
        var feedbackText = FeedbackText.Create("My feedback").Value;
        var rating = FeedbackRating.Create(4).Value;
        var creatorUserId = Guid.CreateVersion7();
        var differentUserId = Guid.CreateVersion7();
        var feedback = Feedback.Create(feedbackText, rating, creatorUserId).Value;

        var newText = FeedbackText.Create("Hacked feedback").Value;
        var newRating = FeedbackRating.Create(1).Value;

        // Act
        var changeResult = feedback.ChangeFeedback(newText, newRating, differentUserId);

        // Assert
        using (new AssertionScope())
        {
            changeResult.Should().BeFailure();
            var forbiddenError = changeResult.Errors[0] as ForbiddenError;
            forbiddenError.Should().NotBeNull();
            forbiddenError!.ErrorCode.Should().Be("WeatherFeedback.Forbidden");
        }
    }

    [Fact]
    public void ChangeFeedback_WhenValuesChanged_RaisesFeedbackChangedDomainEvent()
    {
        // Arrange
        var originalText = FeedbackText.Create("Original").Value;
        var originalRating = FeedbackRating.Create(2).Value;
        var userId = Guid.CreateVersion7();
        var feedback = Feedback.Create(originalText, originalRating, userId).Value;
        _ = feedback.PopDomainEvents(); // Clear the created event

        var newText = FeedbackText.Create("New feedback").Value;
        var newRating = FeedbackRating.Create(4).Value;

        // Act
        var changeResult = feedback.ChangeFeedback(newText, newRating, userId);

        // Assert
        using (new AssertionScope())
        {
            changeResult.Should().BeSuccess();
            var domainEvents = feedback.PopDomainEvents();
            domainEvents.Should().ContainSingle();
            var domainEvent = domainEvents[0] as FeedbackChangedDomainEvent;
            domainEvent.Should().NotBeNull();
            domainEvent.FeedbackId.Should().Be(feedback.Id);
            domainEvent.UserId.Should().Be(userId);
            domainEvent.OldText.Should().Be(originalText);
            domainEvent.NewText.Should().Be(newText);
            domainEvent.OldRating.Should().Be(originalRating);
            domainEvent.NewRating.Should().Be(newRating);
        }
    }

    [Fact]
    public void ChangeFeedback_WhenValuesUnchanged_DoesNotRaiseDomainEvent()
    {
        // Arrange
        var feedbackText = FeedbackText.Create("Same feedback").Value;
        var rating = FeedbackRating.Create(3).Value;
        var userId = Guid.CreateVersion7();
        var feedback = Feedback.Create(feedbackText, rating, userId).Value;
        _ = feedback.PopDomainEvents(); // Clear the created event

        // Act
        var changeResult = feedback.ChangeFeedback(feedbackText, rating, userId);

        // Assert
        using (new AssertionScope())
        {
            changeResult.Should().BeSuccess();
            feedback.PopDomainEvents().Should().BeEmpty();
        }
    }
}
