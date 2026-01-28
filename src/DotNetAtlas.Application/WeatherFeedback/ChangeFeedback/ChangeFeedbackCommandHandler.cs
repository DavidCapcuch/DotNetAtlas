using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Feedback.Errors;
using DotNetAtlas.Domain.Feedback.Specifications;
using DotNetAtlas.Domain.Feedback.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherFeedback.ChangeFeedback;

public sealed class ChangeFeedbackCommandHandler : ICommandHandler<ChangeFeedbackCommand>
{
    private readonly ILogger<ChangeFeedbackCommandHandler> _logger;
    private readonly IWeatherDbContext _weatherDbContext;

    public ChangeFeedbackCommandHandler(
        ILogger<ChangeFeedbackCommandHandler> logger,
        IWeatherDbContext weatherDbContext)
    {
        _logger = logger;
        _weatherDbContext = weatherDbContext;
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

        var changeResult = existingFeedback.ChangeFeedback(feedbackResult.Value, ratingResult.Value, command.UserId);
        if (changeResult.IsFailed)
        {
            return Result.Fail(changeResult.Errors);
        }

        await _weatherDbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Updated weather feedback with ID: {FeedbackId}", existingFeedback.Id);

        return Result.Ok();
    }
}
