using Bogus;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using Serilog;

namespace DotNetAtlas.Infrastructure.Persistence.Database.Seed;

/// <summary>
/// See https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding for more information.
/// </summary>
public static class DatabaseSeedExtensions
{
    /// <summary>
    /// Called automatically by EF Core during MigrateAsync.
    /// </summary>
    public static DbContextOptionsBuilder UseAsyncSeeding(this DbContextOptionsBuilder builder)
    {
        builder.UseAsyncSeeding(async (dbContext, _, ct) =>
        {
            await SeedDatabaseAsync(dbContext, ct);
        });

        return builder;
    }

    /// <summary>
    /// Called automatically by EF Core during update-database command.
    /// </summary>
    public static DbContextOptionsBuilder UseSeeding(this DbContextOptionsBuilder builder)
    {
        builder.UseSeeding((dbContext, _) =>
        {
            SeedDatabaseAsync(dbContext).GetAwaiter().GetResult();
        });

        return builder;
    }

    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();

        try
        {
            Log.Logger.Information("Starting database migrations...");
            await dbContext.Database.MigrateAsync();
            Log.Logger.Information("Database migrations completed");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "An error occurred while applying database migrations");
            throw;
        }
    }

    /// <summary>
    /// Seeds the database with 100 initial records if it is empty.
    /// Only used in local and development environments. In other environments,
    /// flyway migrations are used to apply changes, so this seeding isn't called.
    /// </summary>
    private static async Task SeedDatabaseAsync(DbContext dbContext, CancellationToken ct = default)
    {
        using var _ = SuppressInstrumentationScope.Begin();

        // deterministic seed for data consistency
        Randomizer.Seed = new Random(420_69);

        var weatherDbContext = (WeatherDbContext)dbContext;
        var itemsExist = await weatherDbContext.Feedbacks.AnyAsync(ct);
        if (!itemsExist)
        {
            var weatherFeedbackFaker = new WeatherFeedbackFaker();
            var weatherFeedbacksToSeed = weatherFeedbackFaker.Generate(99);

            // for deterministic seed test data in endpoint example
            weatherFeedbackFaker.RuleFor(wf => wf.Id, _ => new Guid("0198B2A9-CB8C-744B-8CDD-0B64727CF2FC"));
            weatherFeedbacksToSeed.AddRange(weatherFeedbackFaker.Generate());

            Log.Logger.Information("Seeding {Count} weather feedbacks", weatherFeedbacksToSeed.Count);
            weatherDbContext.Feedbacks.AddRange(weatherFeedbacksToSeed);
            await weatherDbContext.SaveChangesAsync(ct);
            Log.Logger.Information("Seeded {Count} weather feedbacks", weatherFeedbacksToSeed.Count);
        }
    }
}
