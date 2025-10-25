using DotNetAtlas.Test.Shared.Common;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace DotNetAtlas.Test.Shared.Redis;

/// <summary>
/// Manages a Redis test container with optimal configuration for test scenarios.
/// Encapsulates connection string, configuration options, and reset functionality for test isolation.
/// </summary>
/// <remarks>
/// Keep the container images in sync with production.
/// When upgrading infrastructure, update the images here early to catch breaking changes sooner.
/// </remarks>
public sealed class RedisTestContainer : ITestContainer
{
    private readonly RedisContainer _container;
    private ConnectionMultiplexer _multiplexer = null!;

    public string ImageName => "redis:7.4.2";

    /// <summary>
    /// Gets the Redis connection string for dependency injection.
    /// This is the raw connection string from the container.
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>
    /// Gets ConfigurationOptions made for testing with admin rights.
    /// </summary>
    public ConfigurationOptions ConfigurationOptions { get; private set; } = null!;

    /// <summary>
    /// Creates a preconfigured Redis test container.
    /// </summary>
    public RedisTestContainer()
    {
        _container = new RedisBuilder()
            .WithImage(ImageName)
            .WithName($"TestRedis-{Guid.NewGuid()}")
            .WithCleanUp(true)
            .Build();
    }

    /// <summary>
    /// Starts the Redis container and waits for it to be ready.
    /// Call this during test fixture initialization (e.g., in PreSetupAsync or constructor).
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if StartAsync is called multiple times.</exception>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _container.StartAsync(ct);

        ConnectionString = _container.GetConnectionString();

        ConfigurationOptions = ConfigurationOptions.Parse(ConnectionString);
        ConfigurationOptions.AllowAdmin = true;
        ConfigurationOptions.DefaultDatabase = 0;
        ConfigurationOptions.AbortOnConnectFail = false;
        ConfigurationOptions.ConnectRetry = 5;
        ConfigurationOptions.ConnectTimeout = 15000;
        ConfigurationOptions.SyncTimeout = 10000;
        ConfigurationOptions.KeepAlive = 60;

        _multiplexer = await ConnectionMultiplexer.ConnectAsync(ConfigurationOptions);
    }

    /// <summary>
    /// Flushes all Redis databases to ensure test isolation.
    /// Call between tests to reset state (e.g., in test teardown).
    /// </summary>
    public async Task CleanDataAsync()
    {
        var flushDatabaseTasks = _multiplexer
            .GetServers()
            .Select(server => server.FlushAllDatabasesAsync());

        await Task.WhenAll(flushDatabaseTasks);
    }

    /// <summary>
    /// Stops and disposes the Redis container.
    /// Call this during test fixture teardown (e.g., in TearDownAsync or Dispose).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _multiplexer.DisposeAsync();
        await _container.DisposeAsync();
    }
}
