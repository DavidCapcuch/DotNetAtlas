using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherFeedback.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Domain.Entities.Weather.Feedback.Errors;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherFeedback.SendFeedback;

public class SendFeedbackCommandHandler : ICommandHandler<SendFeedbackCommand, Guid>
{
    private readonly ILogger<SendFeedbackCommandHandler> _logger;
    private readonly IWeatherContext _weatherContext;

    public SendFeedbackCommandHandler(
        ILogger<SendFeedbackCommandHandler> logger,
        IWeatherContext weatherContext)
    {
        _logger = logger;
        _weatherContext = weatherContext;
    }

    public async Task<Result<Guid>> HandleAsync(
        SendFeedbackCommand command,
        CancellationToken ct)
    {
        var existingFeedback = await _weatherContext.Feedbacks
            .WithSpecification(new WeatherFeedbackByUserIdSpec(command.UserId))
            .FirstOrDefaultAsync(ct);
        if (existingFeedback is not null)
        {
            return Result.Fail(FeedbackErrors.Conflict(existingFeedback.Id));
        }

        var ratingResult = FeedbackRating.Create(command.Rating);
        var feedbackResult = FeedbackText.Create(command.Feedback);
        var merged = Result.Merge(ratingResult, feedbackResult);
        if (merged.IsFailed)
        {
            return Result.Fail(merged.Errors);
        }

        var weatherFeedback =
            new Feedback(feedbackResult.Value, ratingResult.Value,
                command.UserId);
        _weatherContext.Feedbacks.Add(weatherFeedback);
        await _weatherContext.SaveChangesAsync(ct);

        _logger.LogInformation("Weather feedback created with ID: {FeedbackId}", weatherFeedback.Id);
        Activity.Current?.SetTag(DiagnosticNames.FeedbackId, weatherFeedback.Id);

        return Result.Ok(weatherFeedback.Id);
    }
}
