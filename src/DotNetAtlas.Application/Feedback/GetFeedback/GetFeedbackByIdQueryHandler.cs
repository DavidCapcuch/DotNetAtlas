using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.Feedback.Common.Specifications;
using DotNetAtlas.Domain.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Application.Feedback.GetFeedback;

public class GetFeedbackByIdQueryHandler : IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse>
{
    private readonly IWeatherForecastContext _weatherForecastContext;

    public GetFeedbackByIdQueryHandler(
        IWeatherForecastContext weatherForecastContext)
    {
        _weatherForecastContext = weatherForecastContext;
    }

    public async Task<Result<GetFeedbackByIdResponse>> HandleAsync(
        GetFeedbackByIdQuery query,
        CancellationToken ct)
    {
        Activity.Current?.SetTag(DiagnosticNames.FeedbackId, query.Id.ToString());

        var response = await _weatherForecastContext.WeatherFeedbacks
            .AsNoTracking()
            .WithSpecification(new WeatherFeedbackByIdSpec(query.Id))
            .ProjectToFeedbackResponse()
            .FirstOrDefaultAsync(ct);

        if (response is null)
        {
            return Result.Fail(WeatherFeedbackErrors.NotFound(query.Id));
        }

        return response;
    }
}
