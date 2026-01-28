using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts.Specifications;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.ProcessExpiredSubscriptions;

/// <summary>
/// Handles processing of expired premium subscriptions by downgrading them to free tier.
/// Processes a single batch per invocation - schedule the job frequently for continuous processing.
/// Email notification is handled by <see cref="SubscriptionDowngradedSendDowngradedEmailDomainEventHandler"/> via domain events.
/// </summary>
public sealed class ProcessExpiredSubscriptionsCommandHandler : ICommandHandler<ProcessExpiredSubscriptionsCommand>
{
    private readonly IWeatherDbContext _weatherDbContext;
    private readonly ILogger<ProcessExpiredSubscriptionsCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public ProcessExpiredSubscriptionsCommandHandler(
        IWeatherDbContext weatherDbContext,
        TimeProvider timeProvider,
        ILogger<ProcessExpiredSubscriptionsCommandHandler> logger)
    {
        _weatherDbContext = weatherDbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ProcessExpiredSubscriptionsCommand command, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow();

        var expiredSubscribers = await _weatherDbContext.AlertSubscribers
            .WithSpecification(new ExpiredPremiumSubscribersSpec(utcNow))
            .OrderBy(s => s.SubscriptionExpiryAtUtc)
            .Take(command.BatchSize)
            .ToArrayAsync(ct);

        if (expiredSubscribers.Length == 0)
        {
            _logger.LogDebug("No expired subscriptions found");
            return Result.Ok();
        }

        _logger.LogInformation("Processing {Count} expired subscribers", expiredSubscribers.Length);

        var downgradedCount = 0;
        foreach (var subscriber in expiredSubscribers)
        {
            var downgradeResult = subscriber.DowngradeToFree(utcNow);
            if (downgradeResult.IsFailed)
            {
                _logger.LogWarning(
                    "Failed to downgrade subscriber {SubscriberId}: {Errors}",
                    subscriber.Id, downgradeResult.Errors.ToErrorsSummary());
                continue;
            }

            downgradedCount++;
        }

        await _weatherDbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Processed {ProcessedCount} expired subscriptions, downgraded {DowngradedCount}",
            expiredSubscribers.Length, downgradedCount);

        return Result.Ok();
    }
}
