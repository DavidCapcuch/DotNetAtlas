using FluentValidation;

namespace DotNetAtlas.Application.Forecast.GetForecasts;

public class GetForecastQueryValidator : AbstractValidator<GetForecastQuery>
{
    public GetForecastQueryValidator()
    {
        RuleFor(gfr => gfr.Days)
            .InclusiveBetween(1, 14)
                .WithMessage("Days must be between 1 and 14.");

        RuleFor(gfr => gfr.City)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(100);
    }
}
