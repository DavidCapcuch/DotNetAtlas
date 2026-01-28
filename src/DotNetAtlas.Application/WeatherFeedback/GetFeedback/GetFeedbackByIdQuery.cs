using DotNetAtlas.CQS;
using FastEndpoints;

namespace DotNetAtlas.Application.WeatherFeedback.GetFeedback;

public class GetFeedbackByIdQuery : IQuery<GetFeedbackByIdResponse>
{
    /// <summary>
    /// ID of requested feedback.
    /// </summary>
    [RouteParam]
    public required Guid Id { get; set; }
}
