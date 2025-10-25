using DotNetAtlas.Application.Common.Validators;
using FluentValidation;

namespace DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromCityAlerts;

public class UnsubscribeFromCityAlertsCommandValidator : AbstractValidator<UnsubscribeFromCityAlertsCommand>
{
    public UnsubscribeFromCityAlertsCommandValidator()
    {
        RuleFor(ufcac => ufcac.City)
            .SetValidator(new CityValidator());
        RuleFor(ufcac => ufcac.CountryCode).IsInEnum();
        RuleFor(ufcac => ufcac.ConnectionId)
            .NotEmpty();
    }
}
