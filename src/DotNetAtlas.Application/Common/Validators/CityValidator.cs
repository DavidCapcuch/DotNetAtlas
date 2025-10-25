using FluentValidation;

namespace DotNetAtlas.Application.Common.Validators;

public sealed class CityValidator : AbstractValidator<string>
{
    public CityValidator()
    {
        RuleFor(city => city)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(100);
    }
}
