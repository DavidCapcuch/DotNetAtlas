using DotNetAtlas.Application.Common.Validators;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using FluentValidation;

namespace DotNetAtlas.Application.WeatherForecast.GetForecasts;

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
