using FastEndpoints;
using Platform.CQS;

namespace Weather.Application.WeatherFeedback.GetFeedback;

public class GetFeedbackByIdQuery : IQuery<GetFeedbackByIdResponse>
{
    /// <summary>
    /// ID of requested feedback.
    /// </summary>
    [RouteParam]
    public required Guid Id { get; set; }
}
