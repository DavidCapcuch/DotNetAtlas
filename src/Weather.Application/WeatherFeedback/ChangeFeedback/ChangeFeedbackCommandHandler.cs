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

namespace Weather.Application.WeatherFeedback.ChangeFeedback;

public sealed class ChangeFeedbackCommandHandler : ICommandHandler<ChangeFeedbackCommand>
{
    private readonly ILogger<ChangeFeedbackCommandHandler> _logger;
    private readonly IWeatherDbContext _weatherDbContext;
    private readonly TimeProvider _timeProvider;

    public ChangeFeedbackCommandHandler(
        ILogger<ChangeFeedbackCommandHandler> logger,
        IWeatherDbContext weatherDbContext,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _weatherDbContext = weatherDbContext;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(
        ChangeFeedbackCommand command,
        CancellationToken ct)
    {
        Activity.Current?.SetTag(TraceTags.FeedbackId, command.Id.ToString());

        var ratingResult = FeedbackRating.Create(command.Rating);
        var feedbackResult = FeedbackText.Create(command.Feedback);
        var mergedResults = Result.Merge(ratingResult, feedbackResult);
        if (mergedResults.IsFailed)
        {
            return Result.Fail(mergedResults.Errors);
        }

        var existingFeedback = await _weatherDbContext.Feedbacks
            .WithSpecification(new FeedbackByIdSpec(command.Id))
            .FirstOrDefaultAsync(ct);

        if (existingFeedback is null)
        {
            return Result.Fail(FeedbackErrors.NotFound(command.Id));
        }

        var changeResult = existingFeedback.ChangeFeedback(
            feedbackResult.Value, ratingResult.Value, command.UserId, _timeProvider.GetUtcNow());
        if (changeResult.IsFailed)
        {
            return Result.Fail(changeResult.Errors);
        }

        await _weatherDbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Updated weather feedback with ID: {FeedbackId}", existingFeedback.Id);

        return Result.Ok();
    }
}
