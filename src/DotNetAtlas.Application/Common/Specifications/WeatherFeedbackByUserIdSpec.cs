using Ardalis.Specification;
using DotNetAtlas.Domain.Entities.Weather.Feedback;

namespace DotNetAtlas.Application.Common.Specifications;

public class WeatherFeedbackByUserIdSpec : Specification<WeatherFeedback>
{
    public WeatherFeedbackByUserIdSpec(Guid userId)
    {
        Query.Where(wf => wf.CreatedByUser == userId)
            .TagWith(nameof(WeatherFeedbackByUserIdSpec));
    }
}
