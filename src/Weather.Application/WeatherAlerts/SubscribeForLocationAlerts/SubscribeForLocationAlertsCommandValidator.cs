using FluentValidation;
using Weather.Application.Common.Validators;

namespace Weather.Application.WeatherAlerts.SubscribeForLocationAlerts;

public class SubscribeForLocationAlertsCommandValidator : AbstractValidator<SubscribeForLocationAlertsCommand>
{
    public SubscribeForLocationAlertsCommandValidator()
    {
        RuleFor(sfcac => sfcac.City).SetValidator(new CityValidator());
        RuleFor(sfcac => sfcac.CountryCode).IsInEnum();
        RuleFor(sfcac => sfcac.ConnectionId).NotEmpty();
    }
}
