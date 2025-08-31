using DotNetAtlas.Application.Feedback.GetFeedback;
using DotNetAtlas.Domain.Entities.Weather;
using Riok.Mapperly.Abstractions;

namespace DotNetAtlas.Application.Feedback;

[Mapper]
public static partial class WeatherFeedbackMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(WeatherFeedback.Feedback.Value), nameof(GetFeedbackByIdResponse.Feedback))]
    [MapProperty(nameof(WeatherFeedback.Rating.Value), nameof(GetFeedbackByIdResponse.Rating))]
    public static partial GetFeedbackByIdResponse ToFeedbackResponse(this WeatherFeedback activeCalloutItem);

    public static partial IQueryable<GetFeedbackByIdResponse> ProjectToFeedbackResponse(
        this IQueryable<WeatherFeedback> activeCalloutItem);
}
