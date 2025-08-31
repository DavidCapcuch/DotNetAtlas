using System.Net;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Application.Feedback.ChangeFeedback;
using FastEndpoints;
using Serilog.Context;

namespace DotNetAtlas.Api.Endpoints.Weather;

internal class ChangeFeedbackEndpoint : Endpoint<ChangeFeedbackCommand>
{
    private readonly Application.Common.CQS.ICommandHandler<ChangeFeedbackCommand> _changeFeedbackHandler;

    public ChangeFeedbackEndpoint(
        Application.Common.CQS.ICommandHandler<ChangeFeedbackCommand> changeFeedbackHandler)
    {
        _changeFeedbackHandler = changeFeedbackHandler;
    }

    public override void Configure()
    {
        Put("feedback/{id}");
        Version(1);
        Group<WeatherGroup>();
        Summary(s =>
        {
            s.Summary = "Change weather feedback. Only user who created the feedback can change it.";
            s.ExampleRequest = new ChangeFeedbackCommand
            {
                Id = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"), // from deterministic seed test data
                Feedback = "Nevermind. Promised sun, delivered ocean. Boss music started and my picnic learned to swim",
                Rating = 1
            };
        });
        Description(b =>
        {
            b.Produces((int) HttpStatusCode.NotFound);
            b.Produces((int) HttpStatusCode.Forbidden);
        });
    }

    public override async Task HandleAsync(ChangeFeedbackCommand sendFeedbackCommand, CancellationToken ct)
    {
        using var _ = LogContext.PushProperty("FeedbackId", sendFeedbackCommand.Id.ToString());

        var changeFeedbackResult = await _changeFeedbackHandler.HandleAsync(sendFeedbackCommand, ct);

        await changeFeedbackResult.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
