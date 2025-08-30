using DotNetAtlas.Application.Common.CQS;
using FastEndpoints;

namespace DotNetAtlas.Application.Feedback.GetFeedback
{
    public class GetFeedbackByIdQuery : IQuery<GetFeedbackByIdResponse>
    {
        /// <summary>
        /// ID of requested feedback.
        /// </summary>
        [RouteParam]
        public required Guid Id { get; set; }
    }
}