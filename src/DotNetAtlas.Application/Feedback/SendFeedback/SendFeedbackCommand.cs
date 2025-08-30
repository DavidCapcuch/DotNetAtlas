using System.Security.Claims;
using DotNetAtlas.Application.Common.CQS;

namespace DotNetAtlas.Application.Feedback.SendFeedback
{
    public class SendFeedbackCommand : ICommand<Guid>
    {
        /// <summary>
        /// Feedback message about the weather forecast.
        /// </summary>
        public required string Feedback { get; set; }
        public required byte Rating { get; set; }

        [FastEndpoints.FromClaim(ClaimTypes.NameIdentifier)]
        public Guid UserId { get; set; }
    }
}