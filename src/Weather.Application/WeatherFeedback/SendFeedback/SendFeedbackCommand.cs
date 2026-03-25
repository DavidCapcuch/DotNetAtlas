using System.Security.Claims;
using FastEndpoints;
using Platform.CQS;

namespace Weather.Application.WeatherFeedback.SendFeedback;

public class SendFeedbackCommand : ICommand<Guid>
{
    /// <summary>
    /// Feedback message about the weather forecast.
    /// </summary>
    public required string Feedback { get; set; }

    public required byte Rating { get; set; }

    [FromClaim(ClaimTypes.NameIdentifier, true, true)]
    [HideFromDocs]
    public Guid UserId { get; set; }
}
