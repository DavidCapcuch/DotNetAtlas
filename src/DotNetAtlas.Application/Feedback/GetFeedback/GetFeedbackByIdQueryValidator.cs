using FluentValidation;

namespace DotNetAtlas.Application.Feedback.GetFeedback;

public class GetFeedbackByIdQueryValidator : AbstractValidator<GetFeedbackByIdQuery>
{
    public GetFeedbackByIdQueryValidator()
    {
        RuleFor(gfr => gfr.Id)
            .NotEmpty()
            .WithMessage("Feedback ID must not be empty.");
    }
}
