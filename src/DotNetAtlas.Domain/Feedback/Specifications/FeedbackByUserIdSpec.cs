using Ardalis.Specification;

namespace DotNetAtlas.Domain.Feedback.Specifications;

public sealed class FeedbackByUserIdSpec : Specification<Feedback>
{
    public FeedbackByUserIdSpec(Guid userId)
    {
        Query.Where(wf => wf.CreatedByUser == userId)
            .TagWith(nameof(FeedbackByUserIdSpec));
    }
}
