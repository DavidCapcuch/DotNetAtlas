using System.Net;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Application.Feedback.GetFeedback;
using DotNetAtlas.Application.Feedback.SendFeedback;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Weather
{
    public class SendFeedbackEndpoint : Endpoint<SendFeedbackCommand>
    {
        private readonly ILogger<SendFeedbackEndpoint> _logger;
        private readonly Application.Common.CQS.ICommandHandler<SendFeedbackCommand, Guid> _sendFeedbackHandler;

        public SendFeedbackEndpoint(
            ILogger<SendFeedbackEndpoint> logger,
            Application.Common.CQS.ICommandHandler<SendFeedbackCommand, Guid> sendFeedbackHandler)
        {
            _logger = logger;
            _sendFeedbackHandler = sendFeedbackHandler;
        }

        public override void Configure()
        {
            Post("feedback");
            Version(1);
            Group<WeatherGroup>();
            Summary(s =>
            {
                s.Summary = "Send weather forecast feedback.";
                s.ExampleRequest = new SendFeedbackCommand
                {
                    Feedback = "The greatest weather forecast ever, saved me from a tornado!",
                    Rating = 5,
                    UserId = new Guid("14ebd293-d020-4729-a155-6b177d34f36f")
                };
            });
            Description(b =>
            {
                b.ClearDefaultProduces((int) HttpStatusCode.NoContent);
                b.Produces((int) HttpStatusCode.Created);
                b.Produces((int) HttpStatusCode.Conflict);
            });
        }

        public override async Task HandleAsync(SendFeedbackCommand sendFeedbackCommand, CancellationToken ct)
        {
            var sendFeedbackResult = await _sendFeedbackHandler.HandleAsync(sendFeedbackCommand, ct);

            await sendFeedbackResult.MatchAsync(
                id => Send.CreatedAtAsync<GetFeedbackByIdEndpoint>(
                    new GetFeedbackByIdQuery
                    {
                        Id = id
                    }, cancellation: ct),
                failureResult => Send.SendErrorResponseAsync(failureResult, ct));
        }
    }
}