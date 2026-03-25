using FluentValidation;
using Weather.Application.Common.Validators;
using Weather.Domain.Forecast.ValueObjects;

namespace Weather.Application.WeatherForecast.GetForecasts;

public class GetForecastQueryValidator : AbstractValidator<GetForecastQuery>
{
    public GetForecastQueryValidator()
    {
        RuleFor(gfr => gfr.Days)
            .InclusiveBetween(ForecastCriteria.MinDays, ForecastCriteria.MaxDays)
                .WithMessage($"Days must be between {ForecastCriteria.MinDays} and {ForecastCriteria.MaxDays}.");
        RuleFor(gfr => gfr.CountryCode).IsInEnum();
        RuleFor(gfr => gfr.City)
            .SetValidator(new CityValidator());
    }
}
