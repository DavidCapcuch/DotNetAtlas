using DotNetAtlas.Domain.Feedback;
using Riok.Mapperly.Abstractions;

namespace DotNetAtlas.Application.WeatherFeedback.GetFeedback;

[Mapper]
public static partial class GetFeedbackMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(Feedback.FeedbackText.Text), nameof(GetFeedbackByIdResponse.Feedback))]
    [MapProperty(nameof(Feedback.Rating.Value), nameof(GetFeedbackByIdResponse.Rating))]
    public static partial GetFeedbackByIdResponse ToGetFeedbackByIdResponse(this Feedback source);

    public static partial IQueryable<GetFeedbackByIdResponse> ProjectToFeedbackResponse(
        this IQueryable<Feedback> source);
}
