using FastEndpoints;
using Platform.CQRS;

namespace Weather.Application.WeatherFeedback.GetFeedback;

public class GetFeedbackByIdQuery : IQuery<GetFeedbackByIdResponse>
{
    /// <summary>
    /// ID of requested feedback.
    /// </summary>
    [RouteParam]
    public required Guid Id { get; set; }
}
