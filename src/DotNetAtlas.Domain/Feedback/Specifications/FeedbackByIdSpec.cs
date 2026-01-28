using Ardalis.Specification;

namespace DotNetAtlas.Domain.Feedback.Specifications;

public sealed class FeedbackByIdSpec : Specification<Feedback>
{
    public FeedbackByIdSpec(Guid id)
    {
        Query.Where(wf => wf.Id == id)
            .TagWith(nameof(FeedbackByIdSpec));
    }
}
