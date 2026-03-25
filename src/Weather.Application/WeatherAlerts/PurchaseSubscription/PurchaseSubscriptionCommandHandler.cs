using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQS;
using Weather.Application.Common.Data;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.Specifications;

namespace Weather.Application.WeatherAlerts.PurchaseSubscription;

/// <summary>
/// Handler for processing subscription purchases.
/// Creates or activates a subscriber to the purchased tier (Pro/Ultra).
/// </summary>
public sealed class
    PurchaseSubscriptionCommandHandler : ICommandHandler<PurchaseSubscriptionCommand>
{
    private readonly IWeatherDbContext _weatherDbContext;
    private readonly ILogger<PurchaseSubscriptionCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public PurchaseSubscriptionCommandHandler(
        IWeatherDbContext weatherDbContext,
        ILogger<PurchaseSubscriptionCommandHandler> logger,
        TimeProvider timeProvider)
    {
        _weatherDbContext = weatherDbContext;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(PurchaseSubscriptionCommand command,
        CancellationToken ct)
    {
        var subscriber = await _weatherDbContext.AlertSubscribers
            .WithSpecification(new SubscriberByUserIdSpec(command.UserId))
            .FirstOrDefaultAsync(ct);

        var utcNow = _timeProvider.GetUtcNow();

        if (subscriber is null)
        {
            // New user purchasing directly - create with paid subscription
            subscriber = AlertSubscriber.CreateWithPaidSubscription(
                command.UserId, command.CorrelationId, command.PaymentTransactionId,
                command.Tier, command.DurationDays, utcNow);
            _weatherDbContext.AlertSubscribers.Add(subscriber);
        }
        else
        {
            // Existing subscriber - activate/upgrade/reactivate
            // Note: ActivatePaidSubscription throws DataIntegrityException for invalid tier transitions
            // These are caught by DeadLetterMiddleware and sent to DLT.
            subscriber.ActivatePaidSubscription(
                command.CorrelationId, command.PaymentTransactionId,
                command.Tier, command.DurationDays, utcNow);
        }

        _logger.LogInformation(
            "Activated paid subscription for subscriber {SubscriberId} (UserId: {UserId}) to {Tier} tier, duration: {DurationDays} days",
            subscriber.Id, command.UserId, command.Tier, command.DurationDays);

        await _weatherDbContext.SaveChangesAsync(ct);

        return Result.Ok();
    }
}
