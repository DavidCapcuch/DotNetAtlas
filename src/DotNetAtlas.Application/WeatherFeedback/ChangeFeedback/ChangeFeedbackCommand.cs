using System.Security.Claims;
using DotNetAtlas.CQS;
using FastEndpoints;

namespace DotNetAtlas.Application.WeatherFeedback.ChangeFeedback;

public class ChangeFeedbackCommand : ICommand
{
    [RouteParam]
    public required Guid Id { get; set; }

    /// <summary>
    /// Feedback message about the weather forecast.
    /// </summary>
    public required string Feedback { get; set; }

    public required byte Rating { get; set; }

    [FromClaim(ClaimTypes.NameIdentifier, true, true)]
    [HideFromDocs]
    public Guid UserId { get; set; }
}
