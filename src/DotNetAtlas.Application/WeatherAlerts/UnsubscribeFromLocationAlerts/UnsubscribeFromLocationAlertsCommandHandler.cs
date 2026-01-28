using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.Domain.Alerts.Specifications;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Domain.Common.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromLocationAlerts;

/// <summary>
/// Handles unsubscription requests from weather alerts at a specific location.
/// </summary>
/// <remarks>
/// <para>
/// This handler removes the connection from the SignalR group via <see cref="IWeatherAlertBroadcaster"/>
/// and removes the user's persisted subscription.
/// </para>
/// <para>
/// <b>DEMO ONLY:</b> This handler does NOT unschedule fake weather data generation jobs.
/// Jobs are cleaned up only on application startup via <c>RecurringJobsHostedService</c>.
/// This simplifies the implementation for demonstration purposes by avoiding the complexity
/// of tracking subscriber counts in a distributed setting while accounting for app restarts, crashes, etc.
/// </para>
/// </remarks>
public sealed class
    UnsubscribeFromLocationAlertsCommandHandler : ICommandHandler<UnsubscribeFromLocationAlertsCommand>
{
    private readonly IWeatherDbContext _weatherDbContext;
    private readonly IWeatherAlertBroadcaster _weatherAlertBroadcaster;
    private readonly ILogger<UnsubscribeFromLocationAlertsCommandHandler> _logger;

    public UnsubscribeFromLocationAlertsCommandHandler(
        IWeatherDbContext weatherDbContext,
        IWeatherAlertBroadcaster weatherAlertBroadcaster,
        ILogger<UnsubscribeFromLocationAlertsCommandHandler> logger)
    {
        _weatherDbContext = weatherDbContext;
        _weatherAlertBroadcaster = weatherAlertBroadcaster;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        UnsubscribeFromLocationAlertsCommand command,
        CancellationToken ct)
    {
        SetTraceTags(command);

        var cityResult = City.Create(command.City);
        if (cityResult.IsFailed)
        {
            return Result.Fail(cityResult.Errors);
        }

        var alertGroup = AlertGroup.From(cityResult.Value, command.CountryCode);
        await _weatherAlertBroadcaster.RemoveConnectionFromGroupAsync(command.ConnectionId, alertGroup, ct);

        if (command.UserId is Guid userId)
        {
            var monitoredLocation = await _weatherDbContext.MonitoredLocations
                .Include(ml => ml.Location)
                .FirstOrDefaultAsync(ml =>
                    ml.Location.City.Name == command.City &&
                    ml.Location.CountryCode == command.CountryCode, ct);

            if (monitoredLocation is null)
            {
                return Result.Fail(MonitoredLocationErrors.MonitoredLocationNotFound(Guid.Empty));
            }

            var alertSubscriber = await _weatherDbContext.AlertSubscribers
                .WithSpecification(new SubscriberByUserIdSpec(userId))
                .FirstOrDefaultAsync(ct);

            if (alertSubscriber is null)
            {
                return Result.Fail(AlertSubscriberErrors.SubscriberNotFound(userId));
            }

            var unsubscribeResult = alertSubscriber.UnsubscribeFromMonitoredLocation(monitoredLocation.Id);
            if (unsubscribeResult.IsFailed)
            {
                return Result.Fail(unsubscribeResult.Errors);
            }

            await _weatherDbContext.SaveChangesAsync(ct);
        }

        return Result.Ok();
    }

    private static void SetTraceTags(UnsubscribeFromLocationAlertsCommand command)
    {
        Activity.Current?.SetTag(TraceTags.City, command.City);
        Activity.Current?.SetTag(TraceTags.CountryCode, command.CountryCode.ToString());
        Activity.Current?.SetTag(TraceTags.UserId, command.UserId?.ToString());
    }
}
