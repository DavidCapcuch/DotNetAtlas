using System.Security.Claims;
using FastEndpoints;
using ICommand = DotNetAtlas.Application.Common.CQS.ICommand;

namespace DotNetAtlas.Application.Feedback.ChangeFeedback;

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
