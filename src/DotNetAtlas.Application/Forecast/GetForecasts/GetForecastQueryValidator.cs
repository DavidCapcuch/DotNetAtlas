using DotNetAtlas.Application.Common.Validation;
using FluentValidation;

namespace DotNetAtlas.Application.Forecast.GetForecasts;

public class GetForecastQueryValidator : AbstractValidator<GetForecastQuery>
{
    public GetForecastQueryValidator()
    {
        RuleFor(gfr => gfr.Days)
            .InclusiveBetween(1, 14)
                .WithMessage("Days must be between 1 and 14.");
        RuleFor(gfr => gfr.CountryCode).IsInEnum();
        RuleFor(gfr => gfr.City)
            .SetValidator(new CityValidator());
    }
}
