using System.Net;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.WeatherFeedback.GetFeedback;
using FastEndpoints;
using Serilog.Context;

namespace DotNetAtlas.Api.Endpoints.Weather;

internal class GetFeedbackByIdEndpoint : Endpoint<GetFeedbackByIdQuery, GetFeedbackByIdResponse>
{
    private readonly IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse> _getFeedbackByIdHandler;

    public GetFeedbackByIdEndpoint(
        IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse> getFeedbackByIdHandler)
    {
        _getFeedbackByIdHandler = getFeedbackByIdHandler;
    }

    public override void Configure()
    {
        Get("feedback/{id}");
        Version(1);
        Group<WeatherGroup>();
        Summary(s =>
        {
            s.Summary = "Returns weather feedback by ID.";
            s.ExampleRequest = new GetFeedbackByIdQuery
            {
                Id = new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC") // from deterministic seed test data
            };
        });
        Description(b => b.Produces((int)HttpStatusCode.NotFound));
    }

    public override async Task HandleAsync(GetFeedbackByIdQuery query, CancellationToken ct)
    {
        using var _ = LogContext.PushProperty("FeedbackId", query.Id.ToString());

        var getFeedbackResult = await _getFeedbackByIdHandler.HandleAsync(query, ct);

        await getFeedbackResult.MatchAsync(
            feedbackResponse => Send.OkAsync(feedbackResponse, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
