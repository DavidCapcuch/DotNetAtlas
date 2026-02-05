using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability.Tracing;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts;
using DotNetAtlas.Domain.Alerts.Specifications;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Domain.Common.Services;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.SubscribeForLocationAlerts;

/// <summary>
/// Handles subscription requests for weather alerts at a specific location.
/// </summary>
/// <remarks>
/// <para>
/// This handler performs two distinct operations in a single command:
/// </para>
/// <list type="number">
/// <item>
/// <b>Real-time subscription (always):</b> Adds the SignalR connection to the location's alert group
/// for immediate push notifications via <see cref="IWeatherAlertBroadcaster"/>.
/// </item>
/// <item>
/// <b>Email subscription (authenticated users only):</b> Creates a persisted subscription in the database,
/// which enables email notifications when alerts are issued for the location.
/// </item>
/// </list>
/// <para>
/// <b>Design Note (Demonstration Purposes):</b> Email alerts are automatically enabled for all
/// authenticated users who subscribe. This simplification demonstrates the complete notification
/// pipeline (real-time + email) without requiring a separate email opt-in flow.
/// </para>
/// <para>
/// In a production system, consider splitting into <c>SubscribeForRealTimeAlertsCommandHandler</c>
/// and <c>SubscribeForEmailAlertsCommandHandler</c> with an explicit email preference toggle.
/// </para>
/// <para>
/// <b>DEMO ONLY - Fake Weather Data Job Scheduling:</b> On every subscription, this handler schedules
/// a recurring Hangfire job that generates fake weather data and immediately triggers it. Hangfire's
/// <c>AddOrUpdate</c> is idempotent, so duplicate calls just update the existing job. This demonstrates
/// the real-time alert pipeline without requiring actual weather station integrations.
/// </para>
/// <para>
/// Jobs are cleaned up only on application startup via <c>RecurringJobsHostedService</c>, not on
/// unsubscribe or disconnect. This simplifies the implementation for demonstration purposes.
/// </para>
/// </remarks>
public sealed class
    SubscribeForLocationAlertsCommandHandler : ICommandHandler<SubscribeForLocationAlertsCommand>
{
    private readonly IFakeWeatherDataGenerationJobScheduler _fakeWeatherDataGenerationJobScheduler;
    private readonly IWeatherDbContext _weatherDbContext;
    private readonly IWeatherAlertBroadcaster _weatherAlertBroadcaster;
    private readonly LocationFactory _locationFactory;
    private readonly ILogger<SubscribeForLocationAlertsCommandHandler> _logger;

    public SubscribeForLocationAlertsCommandHandler(
        IFakeWeatherDataGenerationJobScheduler fakeWeatherDataGenerationJobScheduler,
        IWeatherDbContext weatherDbContext,
        IWeatherAlertBroadcaster weatherAlertBroadcaster,
        LocationFactory locationFactory,
        ILogger<SubscribeForLocationAlertsCommandHandler> logger)
    {
        _fakeWeatherDataGenerationJobScheduler = fakeWeatherDataGenerationJobScheduler;
        _weatherDbContext = weatherDbContext;
        _weatherAlertBroadcaster = weatherAlertBroadcaster;
        _locationFactory = locationFactory;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(SubscribeForLocationAlertsCommand command, CancellationToken ct)
    {
        SetTraceTags(command);

        var monitoredLocation = await _weatherDbContext.MonitoredLocations
            .Include(ml => ml.Location)
            .FirstOrDefaultAsync(ml =>
                ml.Location.City.Name == command.City &&
                ml.Location.CountryCode == command.CountryCode, ct);

        if (monitoredLocation is null)
        {
            var locationResult = await _locationFactory.CreateAsync(command.City, command.CountryCode, ct);
            if (locationResult.IsFailed)
            {
                return Result.Fail(locationResult.Errors);
            }

            monitoredLocation = MonitoredLocation.CreateWithDefaultThresholds(locationResult.Value);
            _weatherDbContext.MonitoredLocations.Add(monitoredLocation);
        }

        var alertGroup = AlertGroup.From(monitoredLocation.Location);

        // Add connection to SignalR group for real-time alerts
        await _weatherAlertBroadcaster.AddConnectionToGroupAsync(command.ConnectionId, alertGroup, ct);

        // Subscribe user to monitored location (persisted subscription with email alerts enabled)
        // Note: For demonstration purposes, authenticated users are automatically enrolled in email alerts.
        // A production system would allow users to toggle email preferences separately.
        if (command.UserId is Guid userId)
        {
            var subscriber = await _weatherDbContext.AlertSubscribers
                .WithSpecification(new SubscriberByUserIdSpec(userId))
                .FirstOrDefaultAsync(ct);

            if (subscriber is null)
            {
                subscriber = AlertSubscriber.CreateFree(userId);
                _weatherDbContext.AlertSubscribers.Add(subscriber);
            }

            var subscribeResult = subscriber.SubscribeToMonitoredLocation(monitoredLocation.Id);
            if (subscribeResult.IsFailed)
            {
                return Result.Fail(subscribeResult.Errors);
            }
        }

        // DEMO: Schedule and trigger a fake weather data generation job on every subscribe.
        // For simplicity, jobs are cleaned up only on application startup, not on unsubscribe/disconnect.
        _fakeWeatherDataGenerationJobScheduler.EnsureWeatherGenerationJobSchedule(monitoredLocation.Id);
        _fakeWeatherDataGenerationJobScheduler.TriggerFakeWeatherDataGenerationJob(monitoredLocation.Id);

        await _weatherDbContext.SaveChangesAsync(ct);

        return Result.Ok();
    }

    private static void SetTraceTags(SubscribeForLocationAlertsCommand command)
    {
        Activity.Current?.SetTag(TraceTags.City, command.City);
        Activity.Current?.SetTag(TraceTags.CountryCode, command.CountryCode.ToString());
        Activity.Current?.SetTag(TraceTags.UserId, command.UserId?.ToString());
    }
}
