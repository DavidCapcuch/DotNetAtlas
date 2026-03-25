using System.Net;
using FastEndpoints;
using Weather.Api.Common.Extensions;
using Weather.Application.WeatherFeedback.GetFeedback;
using Weather.Application.WeatherFeedback.SendFeedback;

namespace Weather.Api.Endpoints.Weather;

internal class SendFeedbackEndpoint : Endpoint<SendFeedbackCommand>
{
    private readonly Platform.CQS.ICommandHandler<SendFeedbackCommand, Guid> _sendFeedbackHandler;

    public SendFeedbackEndpoint(Platform.CQS.ICommandHandler<SendFeedbackCommand, Guid> sendFeedbackHandler)
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
            b.ClearDefaultProduces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Created);
            b.Produces((int)HttpStatusCode.Conflict);
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
