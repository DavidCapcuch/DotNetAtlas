using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.Domain.Alerts.Specifications;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.ExtendSubscription;

public sealed class ExtendSubscriptionCommandHandler : ICommandHandler<ExtendSubscriptionCommand>
{
    private readonly IWeatherDbContext _weatherDbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExtendSubscriptionCommandHandler> _logger;

    public ExtendSubscriptionCommandHandler(
        IWeatherDbContext weatherDbContext,
        TimeProvider timeProvider,
        ILogger<ExtendSubscriptionCommandHandler> logger)
    {
        _weatherDbContext = weatherDbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ExtendSubscriptionCommand command, CancellationToken ct)
    {
        var alertSubscriber = await _weatherDbContext.AlertSubscribers
            .WithSpecification(new SubscriberByUserIdSpec(command.UserId))
            .FirstOrDefaultAsync(ct);

        if (alertSubscriber is null)
        {
            _logger.LogWarning(
                "Subscriber not found for UserId {UserId}, cannot extend subscription",
                command.UserId);
            return Result.Fail(AlertSubscriberErrors.SubscriberNotFound(command.UserId));
        }

        // Note: ExtendSubscription throws DataIntegrityException for invalid states
        // (e.g., extending free subscription, invalid duration). These are caught by
        // DeadLetterMiddleware and sent to DLT.
        alertSubscriber.ExtendSubscription(
            command.CorrelationId,
            command.PaymentTransactionId,
            command.DurationExtendedDays,
            _timeProvider.GetUtcNow());

        await _weatherDbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Extended subscription for subscriber {SubscriberId} (UserId: {UserId}) by {DurationDays} days",
            alertSubscriber.Id, command.UserId, command.DurationExtendedDays);

        return Result.Ok();
    }
}
