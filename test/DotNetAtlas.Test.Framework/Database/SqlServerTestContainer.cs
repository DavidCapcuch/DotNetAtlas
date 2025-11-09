using DotNetAtlas.Test.Framework.Common;
using EvolveDb;
using Microsoft.Data.SqlClient;
using Respawn;
using Testcontainers.MsSql;

namespace DotNetAtlas.Test.Framework.Database;

/// <summary>
/// Manages a SQL Server test container: creates the database, runs Flyway-style migrations via Evolve, and configures Respawn for fast resets between tests.
/// Encapsulates the connection string and reset functionality for test isolation.
/// </summary>
/// <remarks>
/// Keep the container images in sync with production.
/// When upgrading infrastructure, update the images here early to catch breaking changes sooner.
/// </remarks>
public sealed class SqlServerTestContainer : ITestContainer
{
    private readonly MsSqlContainer _sqlContainer;
    private readonly string _databaseName;
    private readonly string _flywayMigrationsPath;
    private readonly RespawnerOptions _respawnerOptions;
    private Respawner _databaseCleaner = null!;

    public string ImageName => "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    /// <summary>
    /// SQL Server connection string for the created test database.
    /// Use this in your test fixture/DI configuration.
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>
    /// Creates a SQL Server test container with Flyway-style migrations (via Evolve) and Respawn-based cleanup.
    /// </summary>
    /// <param name="databaseName">Database name to create.</param>
    /// <param name="flywayMigrationsPath">Absolute path to the directory containing migration SQL scripts.</param>
    /// <param name="respawnerOptions">RespawnerOptions for configuring database cleanup.</param>
    /// <exception cref="ArgumentException">Thrown when databaseName is null or whitespace, or schemas are empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when schemas or flywayMigrationsPath is null.</exception>
    public SqlServerTestContainer(
        string databaseName,
        string flywayMigrationsPath,
        RespawnerOptions respawnerOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(flywayMigrationsPath);

        _databaseName = databaseName;
        _flywayMigrationsPath = flywayMigrationsPath;
        _respawnerOptions = respawnerOptions;

        _sqlContainer = new MsSqlBuilder()
            .WithImage(ImageName)
            .WithName($"TestSqlServer-{Guid.NewGuid()}")
            .WithPassword("pass123*!QWER")
            .WithCleanUp(true)
            .Build();
    }

    /// <summary>
    /// Starts the SQL Server container, creates the database, and executes Flyway migrations.
    /// Call this during test fixture initialization (e.g., in PreSetupAsync).
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <exception cref="OperationCanceledException">Thrown when a Docker API call gets canceled.</exception>
    /// <exception cref="TaskCanceledException">Thrown when a Testcontainers task gets canceled.</exception>
    /// <exception cref="TimeoutException">Thrown when the wait strategy task gets canceled or the timeout expires.</exception>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _sqlContainer.StartAsync(ct);

        ConnectionString = new SqlConnectionStringBuilder(_sqlContainer.GetConnectionString())
        {
            InitialCatalog = _databaseName,
            Encrypt = false,
            ConnectTimeout = 300,
            ConnectRetryCount = 10,
            MaxPoolSize = 1024,
        }.ToString();

        await SetupDatabase(ct);
        await ExecuteFlywayScriptsAsync(ct);

        _databaseCleaner = await Respawner.CreateAsync(ConnectionString, _respawnerOptions);
    }

    private async Task SetupDatabase(CancellationToken ct)
    {
        await _sqlContainer.ExecScriptAsync($"CREATE DATABASE [{_databaseName}]", ct);
        await _sqlContainer.ExecScriptAsync($"ALTER LOGIN sa WITH DEFAULT DATABASE = [{_databaseName}]", ct);
    }

    /// <summary>
    /// Executes Flyway-style migration scripts using Evolve.
    /// </summary>
    private async Task ExecuteFlywayScriptsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var evolve = new Evolve(connection)
        {
            Locations = [_flywayMigrationsPath]
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
    /// - Optionally resets identity columns to 1 (if withReseed was true).
    /// - Preserves schema structure (tables, columns, constraints remain intact).
    /// - Does NOT drop and recreate the database (faster than full recreation).
    /// </remarks>
    public Task CleanDataAsync()
    {
        return _databaseCleaner.ResetAsync(ConnectionString);
    }

    /// <summary>
    /// Stops and disposes the SQL Server container.
    /// Call this during test fixture teardown (e.g., in TearDownAsync or Dispose).
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return _sqlContainer.DisposeAsync();
    }
}
