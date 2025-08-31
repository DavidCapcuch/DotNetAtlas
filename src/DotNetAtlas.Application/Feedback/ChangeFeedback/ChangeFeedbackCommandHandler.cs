using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather;
using DotNetAtlas.Domain.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.Feedback.ChangeFeedback;

public class ChangeFeedbackCommandHandler : ICommandHandler<ChangeFeedbackCommand>
{
    private readonly ILogger<ChangeFeedbackCommandHandler> _logger;
    private readonly IWeatherForecastContext _weatherForecastContext;

    public ChangeFeedbackCommandHandler(
        ILogger<ChangeFeedbackCommandHandler> logger,
        IWeatherForecastContext weatherForecastContext)
    {
        _logger = logger;
        _weatherForecastContext = weatherForecastContext;
    }

    public async Task<Result> HandleAsync(
        ChangeFeedbackCommand command,
        CancellationToken ct)
    {
        Activity.Current?.SetTag("feedback.id", command.Id.ToString());

        var existingFeedback = await _weatherForecastContext.WeatherFeedbacks
            .WithSpecification(new WeatherFeedbackByIdSpec(command.Id))
            .FirstOrDefaultAsync(ct);

        if (existingFeedback is null)
        {
            return Result.Fail(WeatherFeedbackErrors.NotFound(command.Id));
        }

        if (existingFeedback.CreatedByUser != command.UserId)
        {
            return Result.Fail(WeatherFeedbackErrors.Forbidden(command.Id));
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
        await _weatherForecastContext.SaveChangesAsync(ct);

        _logger.LogInformation("Updated Weather feedback with ID: {FeedbackId}", existingFeedback.Id);

        return Result.Ok();
    }
}
