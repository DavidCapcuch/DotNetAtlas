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

        // Child rules are not enforced by design because we want to process the whole batch of readings
        // even if some are invalid.
    }
}
