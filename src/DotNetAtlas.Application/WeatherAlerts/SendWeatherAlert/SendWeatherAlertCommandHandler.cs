using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;

public class SendWeatherAlertCommandHandler : ICommandHandler<SendWeatherAlertCommand>
{
    private readonly ILogger<SendWeatherAlertCommandHandler> _logger;
    private readonly IWeatherAlertNotifier _weatherAlertNotifier;

    public SendWeatherAlertCommandHandler(
        IWeatherAlertNotifier weatherAlertNotifier,
        ILogger<SendWeatherAlertCommandHandler> logger)
    {
        _weatherAlertNotifier = weatherAlertNotifier;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(SendWeatherAlertCommand command, CancellationToken ct)
    {
        await _weatherAlertNotifier.SendWeatherAlert(
            new WeatherAlert(command.City, command.CountryCode, command.Message));
        _logger.LogInformation("Sent weather alert for {City}:{CountryCode}", command.City, command.CountryCode);

        return Result.Ok();
    }
}
