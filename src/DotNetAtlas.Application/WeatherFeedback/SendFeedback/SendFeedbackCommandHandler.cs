using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability.Tracing;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Feedback;
using DotNetAtlas.Domain.Feedback.Errors;
using DotNetAtlas.Domain.Feedback.Specifications;
using DotNetAtlas.Domain.Feedback.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherFeedback.SendFeedback;

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

        var feedbackCreateResult = Feedback.Create(feedbackResult.Value, ratingResult.Value, command.UserId);
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
