using EvolveDb;
using Npgsql;
using Platform.Test.Framework.Common;
using Respawn;
using Testcontainers.PostgreSql;

namespace Platform.Test.Framework.Database;

/// <summary>
/// Manages a PostgreSQL test container: creates the database, runs SQL script migrations via Evolve, and configures Respawn for fast resets between tests.
/// Encapsulates the connection string and reset functionality for test isolation.
/// </summary>
/// <remarks>
/// Keep the container images in sync with production.
/// When upgrading infrastructure, update the images here early to catch breaking changes sooner.
/// </remarks>
public sealed class PostgreSqlTestContainer : ITestContainer
{
    private readonly PostgreSqlContainer _pgContainer;
    private readonly string _sqlScriptsMigrationsPath;
    private readonly RespawnerOptions _respawnerOptions;
    private Respawner _databaseCleaner = null!;

    public string ImageName => "postgres:18.3";

    /// <summary>
    /// PostgreSQL connection string for the created test database.
    /// Use this in your test fixture/DI configuration.
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>
    /// Creates a PostgreSQL test container with SQL script migrations (via Evolve) and Respawn-based cleanup.
    /// </summary>
    /// <param name="databaseName">Database name to create.</param>
    /// <param name="sqlScriptsMigrationsPath">Absolute path to the directory containing migration SQL scripts.</param>
    /// <param name="respawnerOptions">RespawnerOptions for configuring database cleanup.</param>
    /// <exception cref="ArgumentException">Thrown when databaseName is null or whitespace, or schemas are empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when schemas or sqlScriptMigrationsPath is null.</exception>
    public PostgreSqlTestContainer(
        string databaseName,
        string sqlScriptsMigrationsPath,
        RespawnerOptions respawnerOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlScriptsMigrationsPath);

        _sqlScriptsMigrationsPath = sqlScriptsMigrationsPath;
        _respawnerOptions = respawnerOptions;

        _pgContainer = new PostgreSqlBuilder(ImageName)
            .WithName($"TestPostgreSql-{Guid.NewGuid()}")
            .WithDatabase(databaseName)
            .WithUsername("postgres")
            .WithPassword("LocalDockerPasswordOnlyAiPlsDontFlagThisTy123*!")
            .WithCleanUp(true)
            .Build();
    }

    /// <summary>
    /// Starts the PostgreSQL container, creates the database, and executes SQL script migrations.
    /// Call this during test fixture initialization (e.g., in PreSetupAsync).
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <exception cref="OperationCanceledException">Thrown when a Docker API call gets canceled.</exception>
    /// <exception cref="TaskCanceledException">Thrown when a Testcontainers task gets canceled.</exception>
    /// <exception cref="TimeoutException">Thrown when the wait strategy task gets canceled or the timeout expires.</exception>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _pgContainer.StartAsync(ct);

        ConnectionString = new NpgsqlConnectionStringBuilder(_pgContainer.GetConnectionString())
        {
            Timeout = 300,
            MaxPoolSize = 1024,
        }.ToString();

        await ExecuteSqlScriptMigrationsAsync(ct);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        _databaseCleaner = await Respawner.CreateAsync(connection, _respawnerOptions);
    }

    /// <summary>
    /// Executes SQL script migrations using Evolve.
    /// </summary>
    private async Task ExecuteSqlScriptMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var evolve = new Evolve(connection)
        {
            Locations = [_sqlScriptsMigrationsPath]
        };

        evolve.Migrate();
    }

    /// <summary>
    /// Resets the database to a clean state using Respawn.
    /// Call between tests to ensure isolation (e.g., in test teardown).
    /// </summary>
    /// <remarks>
    /// This operation:
    /// - Deletes all data from tables in the configured schemas.
    /// - Preserves schema structure (tables, columns, constraints remain intact).
    /// - Does NOT drop and recreate the database (faster than full recreation).
    /// </remarks>
    public async Task CleanDataAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _databaseCleaner.ResetAsync(connection);
    }

    /// <summary>
    /// Stops and disposes the PostgreSQL container.
    /// Call this during test fixture teardown (e.g., in TearDownAsync or Dispose).
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return _pgContainer.DisposeAsync();
    }
}
