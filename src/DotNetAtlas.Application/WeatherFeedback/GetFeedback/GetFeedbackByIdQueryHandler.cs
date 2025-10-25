using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherFeedback.Common.Specifications;
using DotNetAtlas.Domain.Entities.Weather.Feedback.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Application.WeatherFeedback.GetFeedback;

public class GetFeedbackByIdQueryHandler : IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse>
{
    private readonly IWeatherContext _weatherContext;

    public GetFeedbackByIdQueryHandler(
        IWeatherContext weatherContext)
    {
        _weatherContext = weatherContext;
    }

    public async Task<Result<GetFeedbackByIdResponse>> HandleAsync(
        GetFeedbackByIdQuery query,
        CancellationToken ct)
    {
        Activity.Current?.SetTag(DiagnosticNames.FeedbackId, query.Id.ToString());

        var response = await _weatherContext.Feedbacks
            .AsNoTracking()
            .WithSpecification(new WeatherFeedbackByIdSpec(query.Id))
            .ProjectToFeedbackResponse()
            .FirstOrDefaultAsync(ct);

        if (response is null)
        {
            return Result.Fail(FeedbackErrors.NotFound(query.Id));
        }

        return response;
    }
}
