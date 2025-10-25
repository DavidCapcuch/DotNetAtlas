using Ardalis.Specification;
using DotNetAtlas.Domain.Entities.Weather.Feedback;

namespace DotNetAtlas.Application.WeatherFeedback.Common.Specifications;

public class WeatherFeedbackByUserIdSpec : Specification<Feedback>
{
    public WeatherFeedbackByUserIdSpec(Guid userId)
    {
        Query.Where(wf => wf.CreatedByUser == userId)
            .TagWith(nameof(WeatherFeedbackByUserIdSpec));
    }
}
