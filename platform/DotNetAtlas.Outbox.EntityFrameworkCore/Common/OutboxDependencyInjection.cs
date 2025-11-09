using Confluent.SchemaRegistry;
using DotNetAtlas.Outbox.EntityFrameworkCore.Core;
using DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.Common;

/// <summary>
/// Extension methods for registering Outbox services.
/// </summary>
public static class OutboxDependencyInjection
{
    /// <summary>
    /// Registers the Outbox pattern for the specified DbContext.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements IOutboxContext.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for outbox registration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutbox<TContext>(
        this IServiceCollection services,
        Action<OutboxDependencyInjectionRegistration> configure)
        where TContext : DbContext, IOutboxDbContext
    {
        var domainEventExtractionCache = new DomainEventExtractionCache();
        var avroMappingCache = new AvroMappingCache();

        var registration = new OutboxDependencyInjectionRegistration(domainEventExtractionCache, avroMappingCache);
        configure(registration);

        if (registration.AvroSerializerOptions is null)
        {
            throw new InvalidOperationException(
                $"Avro serializer options are not configured. Call {nameof(OutboxDependencyInjectionRegistration.ConfigureAvroSerializerConfig)} during AddOutbox(...) configuration.");
        }

        if (registration.SchemaRegistryOptions is null)
        {
            throw new InvalidOperationException(
                $"Schema registry options are not configured. Call {nameof(OutboxDependencyInjectionRegistration.ConfigureSchemaRegistryConfig)} during AddOutbox(...) configuration.");
        }

        domainEventExtractionCache.Build();
        avroMappingCache.Build();

        services.AddSingleton(domainEventExtractionCache);
        services.AddSingleton(avroMappingCache);
        services.TryAddSingleton<ISchemaRegistryClient>(_ =>
            new CachedSchemaRegistryClient(registration.SchemaRegistryOptions));
        services.AddSingleton<AvroSerializer>(sp => new AvroSerializer(
            sp.GetRequiredService<ISchemaRegistryClient>(),
            registration.AvroSerializerOptions));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<OutboxInterceptor>(sp => new OutboxInterceptor(
            sp.GetRequiredService<AvroSerializer>(),
            sp.GetRequiredService<DomainEventExtractionCache>(),
            sp.GetRequiredService<AvroMappingCache>(),
            sp.GetRequiredService<TimeProvider>()
        ));

        return services;
    }

    /// <summary>
    /// Adds Outbox Interceptor to DbContext.
    /// </summary>
    /// <param name="options">The DbContext options builder.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>The options builder for chaining.</returns>
    public static DbContextOptionsBuilder UseOutboxEventsInterceptor(
        this DbContextOptionsBuilder options,
        IServiceProvider serviceProvider)
    {
        var interceptor = serviceProvider.GetRequiredService<OutboxInterceptor>();

        return options.AddInterceptors(interceptor);
    }
}
