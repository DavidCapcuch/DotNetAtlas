using Ardalis.Specification;
using DotNetAtlas.Domain.Entities.Weather;

namespace DotNetAtlas.Application.Common.Specifications
{
    public class WeatherFeedbackByIdSpec : Specification<WeatherFeedback>
    {
        public WeatherFeedbackByIdSpec(Guid id)
        {
            Query.Where(wf => wf.Id == id)
                .TagWith(nameof(WeatherFeedbackByIdSpec));
        }
    }
}