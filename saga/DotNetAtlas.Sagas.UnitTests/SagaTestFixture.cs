using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Config.Kafka;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.Test.Framework.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Sagas.UnitTests;

/// <summary>
/// Provides shared test configuration for saga unit tests.
/// Loads configuration from appsettings.Testing.json to avoid duplication.
/// </summary>
public static class SagaTestFixture
{
    private static readonly Lazy<IConfigurationRoot> Configuration = new(() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Testing.json", optional: false)
            .Build());

    public static IOptions<SagaOptions> CreateSagaOptions() =>
        Options.Create(BindRequiredSection<SagaOptions>(SagaOptions.Section));

    public static IOptions<SagaTopicsOptions> CreateSagaTopicsOptions() =>
        Options.Create(BindRequiredSection<SagaTopicsOptions>(SagaTopicsOptions.Section));

    public static IServiceCollection AddSagaOutboxTestServices(
        this IServiceCollection services,
        string databaseName,
        FakeOutboxWriter fakeOutboxWriter)
    {
        services.AddDbContext<SagaDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        services.AddSingleton<IOutboxWriter>(fakeOutboxWriter);

        return services;
    }

    private static T BindRequiredSection<T>(string sectionPath) =>
        Configuration.Value.GetSection(sectionPath).Get<T>()
        ?? throw new InvalidOperationException(
            $"Failed to bind configuration section '{sectionPath}' to {typeof(T).Name}. " +
            "Verify appsettings.Testing.json contains the required values.");
}
