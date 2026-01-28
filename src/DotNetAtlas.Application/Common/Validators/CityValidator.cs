using DotNetAtlas.Domain.Common.ValueObjects;
using FluentValidation;

namespace DotNetAtlas.Application.Common.Validators;

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
