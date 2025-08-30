using FluentValidation;

namespace DotNetAtlas.Application.Forecast.GetForecasts
{
    public class GetForecastsQueryValidator : AbstractValidator<GetForecastsQuery>
    {
        public GetForecastsQueryValidator()
        {
            RuleFor(gfr => gfr.Days)
                .InclusiveBetween(1, 14)
                    .WithMessage("Days must be between 1 and 14.");
        }
    }
}