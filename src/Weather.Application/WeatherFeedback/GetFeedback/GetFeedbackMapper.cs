using Riok.Mapperly.Abstractions;

namespace Weather.Application.WeatherFeedback.GetFeedback;

[Mapper]
public static partial class GetFeedbackMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(Domain.Feedback.Feedback.FeedbackText.Text), nameof(GetFeedbackByIdResponse.Feedback))]
    [MapProperty(nameof(Domain.Feedback.Feedback.Rating.Value), nameof(GetFeedbackByIdResponse.Rating))]
    public static partial GetFeedbackByIdResponse ToGetFeedbackByIdResponse(this Domain.Feedback.Feedback source);

    public static partial IQueryable<GetFeedbackByIdResponse> ProjectToFeedbackResponse(
        this IQueryable<Domain.Feedback.Feedback> source);
}
