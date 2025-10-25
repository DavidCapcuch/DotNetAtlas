using EvolveDb;
using Microsoft.Data.SqlClient;
using Respawn;
using Testcontainers.MsSql;

namespace DotNetAtlas.Test.Shared.Database;

/// <summary>
/// Manages a SQL Server test container: creates the database, runs Flyway-style migrations via Evolve, and configures Respawn for fast resets between tests.
/// Encapsulates the connection string and reset functionality for test isolation.
/// </summary>
/// <remarks>
/// Keep the container images in sync with production.
/// When upgrading infrastructure, update the images here early to catch breaking changes sooner.
/// </remarks>
public sealed class SqlServerTestContainer : IAsyncDisposable
{
    private const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    private readonly MsSqlContainer _sqlContainer;
    private readonly string _databaseName;
    private readonly string[] _schemas;
    private readonly string _flywayMigrationsPath;
    private readonly bool _withReseed;

    private Respawner _databaseCleaner = null!;

    /// <summary>
    /// SQL Server connection string for the created test database.
    /// Use this in your test fixture/DI configuration.
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>
    /// Creates a SQL Server test container with Flyway-style migrations (via Evolve) and Respawn-based cleanup.
    /// </summary>
    /// <param name="databaseName">Database name to create.</param>
    /// <param name="schemas">Schema names to include in Respawn cleanup. Example: ["dbo", "app"].</param>
    /// <param name="flywayMigrationsPath">Absolute path to the directory containing migration SQL scripts.</param>
    /// <param name="withReseed">Whether to reset identity columns to 1 after cleanup. Default: false.</param>
    /// <exception cref="ArgumentException">Thrown when databaseName is null or whitespace, or schemas is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when schemas or flywayMigrationsPath is null.</exception>
    public SqlServerTestContainer(
        string databaseName,
        string[] schemas,
        string flywayMigrationsPath,
        bool withReseed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentException.ThrowIfNullOrWhiteSpace(flywayMigrationsPath);

        if (schemas.Length == 0)
        {
            throw new ArgumentException("At least one schema is required for Respawner cleanup.", nameof(schemas));
        }

        _databaseName = databaseName;
        _schemas = schemas;
        _flywayMigrationsPath = flywayMigrationsPath;
        _withReseed = withReseed;

        _sqlContainer = new MsSqlBuilder()
            .WithImage(DefaultImage)
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
            ConnectRetryCount = 10,
        }.ToString();

        await SetupDatabase(ct);
        await ExecuteFlywayScriptsAsync(ct);

        _databaseCleaner = await Respawner.CreateAsync(ConnectionString, new RespawnerOptions
        {
            SchemasToInclude = _schemas,
            WithReseed = _withReseed
        });
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
