using Confluent.SchemaRegistry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;

/// <summary>
/// Extension methods for registering Outbox services.
/// </summary>
public static class OutboxDependencyInjection
{
    /// <summary>
    /// Registers the Outbox pattern services.
    /// After calling this method, inject <see cref="ITransactionalOutbox{TContext}"/> in your handlers
    /// to add outbox messages, or use <see cref="IOutboxWriter"/> for factory-created DbContexts.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for outbox registration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddOutbox(outbox =>
    /// {
    ///     outbox.ConfigureMessageOrigin("MyService");
    ///     outbox.ConfigureAvroSerializerConfig(options => { /* ... */ });
    ///     outbox.ConfigureSchemaRegistryConfig(options => { /* ... */ });
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddOutbox(
        this IServiceCollection services,
        Action<OutboxDependencyInjectionRegistration> configure)
    {
        var registration = new OutboxDependencyInjectionRegistration();
        configure(registration);

        if (registration.AvroSerializerOptions is null)
        {
            throw new InvalidOperationException(
                $"Avro serializer options are not configured. " +
                $"Call {nameof(OutboxDependencyInjectionRegistration.ConfigureAvroSerializerConfig)} during AddOutbox(...) configuration.");
        }

        if (registration.SchemaRegistryOptions is null)
        {
            throw new InvalidOperationException(
                $"Schema registry options are not configured. " +
                $"Call {nameof(OutboxDependencyInjectionRegistration.ConfigureSchemaRegistryConfig)} during AddOutbox(...) configuration.");
        }

        services.TryAddSingleton<ISchemaRegistryClient>(_ =>
            new CachedSchemaRegistryClient(registration.SchemaRegistryOptions));
        services.TryAddSingleton<AvroSerializer>(sp => new AvroSerializer(
            sp.GetRequiredService<ISchemaRegistryClient>(),
            registration.AvroSerializerOptions));

        services.TryAddSingleton(TimeProvider.System);

        services.Configure<OutboxOptions>(opts =>
            opts.MessageOrigin = registration.MessageOrigin);

        // Core writer - Singleton (stateless, only depends on singleton services)
        services.TryAddSingleton<IOutboxWriter, OutboxWriter>();

        // Transactional outbox - Scoped (uses scoped DbContext)
        services.TryAddScoped(typeof(ITransactionalOutbox<>), typeof(TransactionalOutbox<>));

        return services;
    }
}
