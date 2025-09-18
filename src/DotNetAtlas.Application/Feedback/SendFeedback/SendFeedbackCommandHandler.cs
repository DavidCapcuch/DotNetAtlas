using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Domain.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.Feedback.SendFeedback;

public class SendFeedbackCommandHandler : ICommandHandler<SendFeedbackCommand, Guid>
{
    private readonly ILogger<SendFeedbackCommandHandler> _logger;
    private readonly IWeatherForecastContext _weatherForecastContext;

    public SendFeedbackCommandHandler(
        ILogger<SendFeedbackCommandHandler> logger,
        IWeatherForecastContext weatherForecastContext)
    {
        _logger = logger;
        _weatherForecastContext = weatherForecastContext;
    }

    public async Task<Result<Guid>> HandleAsync(
        SendFeedbackCommand command,
        CancellationToken ct)
    {
        var existingFeedback = await _weatherForecastContext.WeatherFeedbacks
            .WithSpecification(new WeatherFeedbackByUserIdSpec(command.UserId))
            .FirstOrDefaultAsync(ct);
        if (existingFeedback is not null)
        {
            return Result.Fail(WeatherFeedbackErrors.Conflict(existingFeedback.Id));
        }

        var ratingResult = FeedbackRating.Create(command.Rating);
        var feedbackResult = FeedbackText.Create(command.Feedback);
        var merged = Result.Merge(ratingResult, feedbackResult);
        if (merged.IsFailed)
        {
            return Result.Fail(merged.Errors);
        }

        var weatherFeedback = new WeatherFeedback(feedbackResult.Value, ratingResult.Value, command.UserId);
        _weatherForecastContext.WeatherFeedbacks.Add(weatherFeedback);
        await _weatherForecastContext.SaveChangesAsync(ct);

        _logger.LogInformation("Weather feedback created with ID: {FeedbackId}", weatherFeedback.Id);

        return Result.Ok(weatherFeedback.Id);
    }
}
