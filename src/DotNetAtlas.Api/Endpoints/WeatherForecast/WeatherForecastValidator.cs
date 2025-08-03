using DotNetAtlas.Contracts.ApiContracts.Requests;
using FastEndpoints;
using FluentValidation;

namespace DotNetAtlas.Api.Endpoints.WeatherForecast
{
    public class WeatherForecastRequestValidator : Validator<WeatherForecastRequest>
    {
        public WeatherForecastRequestValidator()
        {
            RuleFor(wfr => wfr.Days)
                .GreaterThan(0)
                .LessThan(15)
                .WithMessage("Days must be between 1 and 14.");
        }
    }
}