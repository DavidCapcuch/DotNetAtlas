using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using FastEndpoints;
using Serilog.Context;

namespace DotNetAtlas.Api.Endpoints.Weather;

internal class GetForecastEndpoint : Endpoint<GetForecastQuery, GetForecastResponse>
{
    private readonly IQueryHandler<GetForecastQuery, GetForecastResponse> _getForecastHandler;

    public GetForecastEndpoint(
        IQueryHandler<GetForecastQuery, GetForecastResponse> getForecastHandler)
    {
        _getForecastHandler = getForecastHandler;
    }

    public override void Configure()
    {
        Get("forecast");
        AllowAnonymous();
        Version(1);
        Group<WeatherGroup>();
        Summary(s =>
        {
            s.Summary = "Returns weather forecast.";
            s.Description = "Provides x days weather forecast for the specified city.";
            s.ExampleRequest = new GetForecastQuery
            {
                Days = 5,
                City = "Prague",
                CountryCode = CountryCode.CZ
            };
        });
    }

    public override async Task HandleAsync(GetForecastQuery query, CancellationToken ct)
    {
        using var _ = LogContext.PushProperty("City", query.City);
        using var __ = LogContext.PushProperty("CountryCode", query.CountryCode);

        var result = await _getForecastHandler.HandleAsync(query, ct);

        await result.MatchAsync(
            successResult => Send.OkAsync(successResult, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
