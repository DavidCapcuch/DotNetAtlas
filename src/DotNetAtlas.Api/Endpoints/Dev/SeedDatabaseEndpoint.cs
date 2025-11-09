using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using FastEndpoints;

namespace DotNetAtlas.Api.Endpoints.Dev;

/// <summary>
/// Seeds the database with a specified number of weather feedbacks for testing purposes.
/// </summary>
internal class SeedDatabaseEndpoint : Endpoint<SeedDatabaseCommand>
{
    private readonly WeatherDbContext _weatherDbContext;
    private readonly ILogger<SeedDatabaseEndpoint> _logger;

    public SeedDatabaseEndpoint(
        ILogger<SeedDatabaseEndpoint> logger,
        WeatherDbContext weatherDbContext)
    {
        _logger = logger;
        _weatherDbContext = weatherDbContext;
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
            req.NumberOfRecords,
            User.Identity?.Name);

        var feedbackFaker = new WeatherFeedbackFaker();
        var weatherFeedbacks = feedbackFaker.Generate(req.NumberOfRecords);
        _weatherDbContext.AddRange(weatherFeedbacks);
        await _weatherDbContext.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
