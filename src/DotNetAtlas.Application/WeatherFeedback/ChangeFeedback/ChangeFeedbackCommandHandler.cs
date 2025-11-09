using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherFeedback.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather.Feedback.Errors;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherFeedback.ChangeFeedback;

public class ChangeFeedbackCommandHandler : ICommandHandler<ChangeFeedbackCommand>
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
        Activity.Current?.SetTag(DiagnosticNames.FeedbackId, command.Id.ToString());

        var existingFeedback = await _weatherDbContext.Feedbacks
            .WithSpecification(new WeatherFeedbackByIdSpec(command.Id))
            .FirstOrDefaultAsync(ct);

        if (existingFeedback is null)
        {
            return Result.Fail(FeedbackErrors.NotFound(command.Id));
        }

        if (existingFeedback.CreatedByUser != command.UserId)
        {
            return Result.Fail(FeedbackErrors.Forbidden(command.Id));
        }

        var ratingResult = FeedbackRating.Create(command.Rating);
        var feedbackResult = FeedbackText.Create(command.Feedback);
        var mergedResults = Result.Merge(ratingResult, feedbackResult);
        if (mergedResults.IsFailed)
        {
            return Result.Fail(mergedResults.Errors);
        }

        existingFeedback.ChangeFeedback(feedbackResult.Value, ratingResult.Value);

        await _weatherDbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Updated weather feedback with ID: {FeedbackId}", existingFeedback.Id);

        return Result.Ok();
    }
}
