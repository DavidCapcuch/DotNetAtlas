using DotNetAtlas.Contracts.ApiContracts.Requests;
using DotNetAtlas.Contracts.ApiContracts.Responses;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.WeatherForecast
{
    public class WeatherForecastEndpoint : Endpoint<WeatherForecastRequest, IAsyncEnumerable<WeatherForecastResponse>>
    {
        private static readonly string[] Summaries =
        [
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        ];

        private readonly ILogger<WeatherForecastEndpoint> _logger;

        public WeatherForecastEndpoint(
            ILogger<WeatherForecastEndpoint> logger)
        {
            _logger = logger;
        }

        public override void Configure()
        {
            Get("forecast");
            Group<WeatherGroup>();
            Summary(s =>
            {
                s.Summary = "Returns weather forecast.";
                s.Description = "Provides x days weather forecast with random temperatures and summaries.";
                s.ExampleRequest = new WeatherForecastRequest { Days = 5 };
            });
            Validator<WeatherForecastRequestValidator>();
        }

        public override async Task HandleAsync(WeatherForecastRequest weatherForecastRequest, CancellationToken ct)
        {
            var forecast = Enumerable.Range(1, weatherForecastRequest.Days)
                .Select(index =>
                    new WeatherForecastResponse
                    {
                        Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                        TemperatureC = Random.Shared.Next(-20, 55),
                        Summary = Summaries[Random.Shared.Next(Summaries.Length)]
                    })
                .ToAsyncEnumerable();

            await Send.OkAsync(forecast, ct);
        }
    }
}