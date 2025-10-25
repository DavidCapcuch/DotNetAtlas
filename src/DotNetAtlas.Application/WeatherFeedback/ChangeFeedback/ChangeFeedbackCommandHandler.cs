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
    private readonly IWeatherContext _weatherContext;

    public ChangeFeedbackCommandHandler(
        ILogger<ChangeFeedbackCommandHandler> logger,
        IWeatherContext weatherContext)
    {
        _logger = logger;
        _weatherContext = weatherContext;
    }

    public async Task<Result> HandleAsync(
        ChangeFeedbackCommand command,
        CancellationToken ct)
    {
        Activity.Current?.SetTag(DiagnosticNames.FeedbackId, command.Id.ToString());

        var existingFeedback = await _weatherContext.Feedbacks
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
        var merged = Result.Merge(ratingResult, feedbackResult);
        if (merged.IsFailed)
        {
            return Result.Fail(merged.Errors);
        }

        existingFeedback.UpdateFeedback(feedbackResult.Value);
        existingFeedback.UpdateRating(ratingResult.Value);
        await _weatherContext.SaveChangesAsync(ct);

        _logger.LogInformation("Updated Weather feedback with ID: {FeedbackId}", existingFeedback.Id);

        return Result.Ok();
    }
}
