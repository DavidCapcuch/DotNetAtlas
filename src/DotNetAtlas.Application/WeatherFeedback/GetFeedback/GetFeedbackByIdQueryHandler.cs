using System.Diagnostics;
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Feedback.Errors;
using DotNetAtlas.Domain.Feedback.Specifications;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Application.WeatherFeedback.GetFeedback;

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
