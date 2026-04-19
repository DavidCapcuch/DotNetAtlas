using Microsoft.EntityFrameworkCore;
using Serilog;

namespace SagaOrchestrators.Common.Persistence.Database;

public static class DatabaseSeedExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();

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
}
