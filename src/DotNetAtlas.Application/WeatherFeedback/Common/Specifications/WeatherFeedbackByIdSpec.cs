using Ardalis.Specification;
using DotNetAtlas.Domain.Entities.Weather.Feedback;

namespace DotNetAtlas.Application.WeatherFeedback.Common.Specifications;

public class WeatherFeedbackByIdSpec : Specification<Feedback>
{
    public WeatherFeedbackByIdSpec(Guid id)
    {
        Query.Where(wf => wf.Id == id)
            .TagWith(nameof(WeatherFeedbackByIdSpec));
    }
}
