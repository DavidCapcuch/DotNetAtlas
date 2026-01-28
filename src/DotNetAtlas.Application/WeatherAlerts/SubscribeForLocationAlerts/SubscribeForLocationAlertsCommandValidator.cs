using DotNetAtlas.Application.Common.Validators;
using FluentValidation;

namespace DotNetAtlas.Application.WeatherAlerts.SubscribeForLocationAlerts;

public class SubscribeForLocationAlertsCommandValidator : AbstractValidator<SubscribeForLocationAlertsCommand>
{
    public SubscribeForLocationAlertsCommandValidator()
    {
        RuleFor(sfcac => sfcac.City).SetValidator(new CityValidator());
        RuleFor(sfcac => sfcac.CountryCode).IsInEnum();
        RuleFor(sfcac => sfcac.ConnectionId).NotEmpty();
    }
}
