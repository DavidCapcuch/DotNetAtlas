using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Dev
{
    /// <summary>
    /// Seeds the database with a specified number of active callout items for testing purposes.
    /// </summary>
    public class SeedDatabaseEndpoint : Endpoint<SeedDatabaseCommand>
    {
        private readonly WeatherForecastContext _weatherForecastContext;
        private readonly ILogger<SeedDatabaseEndpoint> _logger;

        public SeedDatabaseEndpoint(
            ILogger<SeedDatabaseEndpoint> logger,
            WeatherForecastContext weatherForecastContext)
        {
            _logger = logger;
            _weatherForecastContext = weatherForecastContext;
        }

        public override void Configure()
        {
            Post("seed-database");
            Version(1);
            Group<DevGroup>();
            Summary(s =>
            {
                s.Description =
                    "Generates a specified number of weather forecast feedbacks.";
            });
        }

        public override async Task HandleAsync(SeedDatabaseCommand req, CancellationToken ct)
        {
            _logger.LogInformation(
                "Seeding DB with {NumberOfRecords} records by user {User}",
                req.NumberOfRecords, User.Identity?.Name);

            var feedbackFaker = new WeatherFeedbackFaker();
            var weatherFeedbacks = feedbackFaker.Generate(req.NumberOfRecords);
            _weatherForecastContext.AddRange(weatherFeedbacks);
            await _weatherForecastContext.SaveChangesAsync(ct);

            await Send.NoContentAsync(ct);
        }
    }
}