using FluentValidation;
using Weather.Application.Common.Validators;

namespace Weather.Application.WeatherAlerts.UnsubscribeFromLocationAlerts;

public class UnsubscribeFromLocationAlertsCommandValidator : AbstractValidator<UnsubscribeFromLocationAlertsCommand>
{
    public UnsubscribeFromLocationAlertsCommandValidator()
    {
        RuleFor(ufcac => ufcac.City).SetValidator(new CityValidator());
        RuleFor(ufcac => ufcac.CountryCode).IsInEnum();
        RuleFor(ufcac => ufcac.ConnectionId).NotEmpty();
    }
}
