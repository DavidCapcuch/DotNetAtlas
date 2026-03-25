using FluentValidation;
using Weather.Domain.Common.ValueObjects;

namespace Weather.Application.Common.Validators;

public sealed class CityValidator : AbstractValidator<string>
{
    public CityValidator()
    {
        RuleFor(city => city)
            .NotEmpty()
            .MinimumLength(City.MinLength)
            .MaximumLength(City.MaxLength);
    }
}
