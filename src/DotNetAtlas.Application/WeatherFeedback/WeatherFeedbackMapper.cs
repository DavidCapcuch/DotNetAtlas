using DotNetAtlas.Application.WeatherFeedback.GetFeedback;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using Riok.Mapperly.Abstractions;

namespace DotNetAtlas.Application.WeatherFeedback;

[Mapper]
public static partial class WeatherFeedbackMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(Feedback.FeedbackText.Value), nameof(GetFeedbackByIdResponse.Feedback))]
    [MapProperty(nameof(Feedback.Rating.Value), nameof(GetFeedbackByIdResponse.Rating))]
    public static partial GetFeedbackByIdResponse ToFeedbackResponse(this Feedback source);

    public static partial IQueryable<GetFeedbackByIdResponse> ProjectToFeedbackResponse(
        this IQueryable<Feedback> source);
}
