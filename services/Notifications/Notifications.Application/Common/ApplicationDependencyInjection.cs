using Microsoft.Extensions.DependencyInjection;

namespace Notifications.Application.Common;

/// <summary>
/// Root DI entry-point for the Notifications Application layer.
/// Reserved for future CQRS handlers, validators, and projection registrations.
/// Today Notifications is purely Kafka-driven, so this method intentionally registers nothing.
/// </summary>
public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddNotificationsApplication(this IServiceCollection services) => services;
}
