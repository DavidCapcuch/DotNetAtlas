using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;
using DotNetAtlas.Infrastructure.BackgroundJobs.Common;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Infrastructure.BackgroundJobs;

[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail, LogEvents = true)]
internal sealed class FakeWeatherAlertBackgroundJob : IBackgroundJob
{
    private static readonly string[] AlertMessages =
    [
        "Rain expected in the next hour. Keep an umbrella handy.",
        "Clear skies ahead. Perfect time for a walk!",
        "Thunderstorms possible this evening. Stay indoors if possible.",
        "Strong winds today. Secure loose outdoor items.",
        "Light snow in the forecast. Drive carefully.",
        "Heat advisory in effect. Stay hydrated.",
        "Foggy conditions expected tomorrow morning.",
        "Cold front approaching. Expect a temperature drop.",
        "UV index is high. Use sunscreen if outdoors.",
        "Pollen count elevated. Allergy sufferers take note."
    ];

    public static string JobId(string id) => $"{nameof(FakeWeatherAlertBackgroundJob)}-{id}";

    private readonly ICommandHandler<SendWeatherAlertCommand> _sendWeatherAlertHandler;
    private readonly ILogger<FakeWeatherAlertBackgroundJob> _logger;

    public FakeWeatherAlertBackgroundJob(
        ILogger<FakeWeatherAlertBackgroundJob> logger,
        ICommandHandler<SendWeatherAlertCommand> sendWeatherAlertHandler)
    {
        _logger = logger;
        _sendWeatherAlertHandler = sendWeatherAlertHandler;
    }

    public async Task SendWeatherAlert(AlertSubscriptionDto dto, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var alertMessage = AlertMessages[Random.Shared.Next(0, AlertMessages.Length)];

        _logger.LogInformation("Notifying {City}:{CountryCode} about weather alert", dto.City, dto.CountryCode);

        await _sendWeatherAlertHandler.HandleAsync(
            new SendWeatherAlertCommand
            {
                City = dto.City,
                CountryCode = dto.CountryCode,
                Message = alertMessage
            }, ct);
    }
}
