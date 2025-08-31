using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.Forecast.GetForecasts;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Weather;

internal class GetForecastsEndpoint : Endpoint<GetForecastsQuery, GetForecastsResponse>
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    private readonly IDotNetAtlasInstrumentation _instrumentation;

    public GetForecastsEndpoint(
        IDotNetAtlasInstrumentation instrumentation)
    {
        _instrumentation = instrumentation;
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
            s.Description = "Provides x days weather forecast with random temperatures and summaries.";
            s.ExampleRequest = new GetForecastsQuery { Days = 5 };
        });
        Validator<GetForecastsQueryValidator>();
    }

    public override async Task HandleAsync(GetForecastsQuery forecastsQuery, CancellationToken ct)
    {
        using var activity = _instrumentation.StartActivity("GetForecasts");
        var forecasts = Enumerable.Range(1, forecastsQuery.Days)
            .Select(index =>
                new ForecastResponse
                {
                    Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    TemperatureC = Random.Shared.Next(-20, 55),
                    Summary = Summaries[Random.Shared.Next(Summaries.Length)]
                })
            .ToAsyncEnumerable();

        var forecastResponses = new GetForecastsResponse
        {
            Forecasts = forecasts
        };

        await Send.OkAsync(forecastResponses, ct);
    }
}
