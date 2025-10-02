using DotNetAtlas.Application.Common.Validation;
using FluentValidation;

namespace DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;

public class SendWeatherAlertCommandValidator : AbstractValidator<SendWeatherAlertCommand>
{
    public SendWeatherAlertCommandValidator()
    {
        RuleFor(swac => swac.City)
            .SetValidator(new CityValidator());
        RuleFor(swac => swac.CountryCode).IsInEnum();
        RuleFor(swac => swac.Message)
            .NotEmpty()
            .MaximumLength(500);
    }
}
