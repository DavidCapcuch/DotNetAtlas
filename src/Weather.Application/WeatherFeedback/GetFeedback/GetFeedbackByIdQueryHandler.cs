using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Platform.CQS;
using Weather.Application.Common.Data;
using Weather.Application.Common.Observability.Tracing;
using Weather.Domain.Feedback.Errors;
using Weather.Domain.Feedback.Specifications;

namespace Weather.Application.WeatherFeedback.GetFeedback;

public sealed class GetFeedbackByIdQueryHandler : IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse>
{
    private readonly IWeatherDbContext _weatherDbContext;

    public GetFeedbackByIdQueryHandler(
        IWeatherDbContext weatherDbContext)
    {
        _weatherDbContext = weatherDbContext;
    }

    public async Task<Result<GetFeedbackByIdResponse>> HandleAsync(
        GetFeedbackByIdQuery query,
        CancellationToken ct)
    {
        Activity.Current?.SetTag(TraceTags.FeedbackId, query.Id.ToString());

        var response = await _weatherDbContext.Feedbacks
            .AsNoTracking()
            .WithSpecification(new FeedbackByIdSpec(query.Id))
            .ProjectToFeedbackResponse()
            .FirstOrDefaultAsync(ct);

        if (response is null)
        {
            return Result.Fail(FeedbackErrors.NotFound(query.Id));
        }

        return response;
    }
}
