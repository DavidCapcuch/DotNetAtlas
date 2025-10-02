using DotNetAtlas.Application.Common.Validation;
using FluentValidation;

namespace DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;

public class SubscribeForCityAlertsCommandValidator : AbstractValidator<SubscribeForCityAlertsCommand>
{
    public SubscribeForCityAlertsCommandValidator()
    {
        RuleFor(sfcac => sfcac.City)
            .SetValidator(new CityValidator());
        RuleFor(sfcac => sfcac.CountryCode).IsInEnum();
        RuleFor(sfcac => sfcac.ConnectionId)
            .NotEmpty();
    }
}
