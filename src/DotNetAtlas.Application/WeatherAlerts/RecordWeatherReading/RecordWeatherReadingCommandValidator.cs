using FluentValidation;

namespace DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;

public class RecordWeatherReadingCommandValidator : AbstractValidator<RecordWeatherReadingCommand>
{
    public RecordWeatherReadingCommandValidator()
    {
        RuleFor(c => c.MonitoredLocationId).NotEmpty();

        RuleFor(c => c.Readings)
            .NotEmpty()
            .WithMessage("At least one weather reading must be provided.");

        RuleForEach(c => c.Readings)
            .ChildRules(reading =>
            {
                reading.RuleFor(r => r.HumidityPercent)
                    .InclusiveBetween(0, 100)
                    .WithMessage("Humidity must be between 0 and 100 percent.");

                reading.RuleFor(r => r.WindSpeedKmh)
                    .GreaterThanOrEqualTo(0)
                    .WithMessage("Wind speed cannot be negative.");

                reading.RuleFor(r => r.RecordedAtUtc).NotEmpty();
            });
    }
}
