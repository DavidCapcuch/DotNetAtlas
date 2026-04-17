using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Weather.Application.Common.Data;
using Weather.Application.Common.Observability.Tracing;
using Weather.Domain.Feedback.Errors;
using Weather.Domain.Feedback.Specifications;
using Weather.Domain.Feedback.ValueObjects;

namespace Weather.Application.WeatherFeedback.SendFeedback;

public sealed class SendFeedbackCommandHandler : ICommandHandler<SendFeedbackCommand, Guid>
{
    private readonly ILogger<SendFeedbackCommandHandler> _logger;
    private readonly IWeatherDbContext _weatherDbContext;

    public SendFeedbackCommandHandler(
        ILogger<SendFeedbackCommandHandler> logger,
        IWeatherDbContext weatherDbContext)
    {
        _logger = logger;
        _weatherDbContext = weatherDbContext;
    }

    public async Task<Result<Guid>> HandleAsync(
        SendFeedbackCommand command,
        CancellationToken ct)
    {
        var ratingResult = FeedbackRating.Create(command.Rating);
        var feedbackResult = FeedbackText.Create(command.Feedback);
        var mergedResults = Result.Merge(ratingResult, feedbackResult);
        if (mergedResults.IsFailed)
        {
            return Result.Fail(mergedResults.Errors);
        }

        var existingFeedback = await _weatherDbContext.Feedbacks
            .WithSpecification(new FeedbackByUserIdSpec(command.UserId))
            .FirstOrDefaultAsync(ct);

        if (existingFeedback is not null)
        {
            return Result.Fail(FeedbackErrors.Conflict(existingFeedback.Id));
        }

        var feedbackCreateResult = Domain.Feedback.Feedback.Create(feedbackResult.Value, ratingResult.Value, command.UserId);
        if (feedbackCreateResult.IsFailed)
        {
            return Result.Fail(feedbackCreateResult.Errors);
        }

        var weatherFeedback = feedbackCreateResult.Value;
        _weatherDbContext.Feedbacks.Add(weatherFeedback);
        await _weatherDbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Weather feedback created with ID: {FeedbackId}", weatherFeedback.Id);
        Activity.Current?.SetTag(TraceTags.FeedbackId, weatherFeedback.Id);

        return Result.Ok(weatherFeedback.Id);
    }
}
