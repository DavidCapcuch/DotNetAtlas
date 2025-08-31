using System.Net;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Application.Feedback.GetFeedback;
using DotNetAtlas.Application.Feedback.SendFeedback;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Weather;

internal class SendFeedbackEndpoint : Endpoint<SendFeedbackCommand>
{
    private readonly Application.Common.CQS.ICommandHandler<SendFeedbackCommand, Guid> _sendFeedbackHandler;

    public SendFeedbackEndpoint(
        Application.Common.CQS.ICommandHandler<SendFeedbackCommand, Guid> sendFeedbackHandler)
    {
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
                Feedback = "Your radar is my spirit animal. Dodged the storm like Neo",
                Rating = 5
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
                },
                cancellation: ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
