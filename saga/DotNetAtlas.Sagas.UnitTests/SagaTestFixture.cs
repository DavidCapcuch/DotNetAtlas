using DotNetAtlas.Sagas.Common.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Sagas.UnitTests;

/// <summary>
/// Provides shared test configuration for saga unit tests.
/// Loads configuration from appsettings.Testing.json to avoid duplication.
/// </summary>
public static class SagaTestFixture
{
    private static readonly Lazy<IOptions<SagaOptions>> CachedSagaOptions = new(LoadSagaOptions);

    /// <summary>
    /// Creates a pre-configured IOptions&lt;SagaOptions&gt; suitable for unit testing.
    /// Loads configuration from appsettings.Testing.json.
    /// </summary>
    public static IOptions<SagaOptions> CreateSagaOptions() => CachedSagaOptions.Value;

    private static IOptions<SagaOptions> LoadSagaOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Testing.json", optional: false)
            .Build();

        var sagaOptions = configuration.GetSection(SagaOptions.Section).Get<SagaOptions>()
            ?? throw new InvalidOperationException("Failed to load SagaOptions from appsettings.Testing.json");

        return Options.Create(sagaOptions);
    }
}
