using AspNetCore.SignalR.OpenTelemetry;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.SignalR;
using Notifications.Api.SignalRHubs;
using Notifications.Application.Bell;

namespace Notifications.Api.Common;

/// <summary>
/// Registers the in-app bell SignalR transport (#316, ADR-0032): the hub server with the
/// MessagePack protocol and OpenTelemetry hub instrumentation, the sub-claim user-id provider that
/// keys per-user groups, and the <see cref="INotificationBroadcaster"/> over the hub context.
/// In-memory backplane only — the Redis backplane (the multi-instance scale-out seam) is ADR-0016
/// and deliberately omitted from this slice.
/// </summary>
internal static class SignalRDependencyInjection
{
    public static IServiceCollection AddNotificationsSignalR(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Registered BEFORE AddSignalR so its TryAddSingleton<IUserIdProvider, DefaultUserIdProvider>
        // is a no-op and this sub-first resolver wins — Context.UserIdentifier must be the Keycloak
        // `sub` (= RecipientUserId) for the per-user group auto-join to target the right recipient.
        services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

        services.AddSignalR()
            .AddMessagePackProtocol(options =>
            {
                options.SerializerOptions = MessagePackSerializerOptions.Standard
                    .WithResolver(ContractlessStandardResolver.Instance)
                    .WithSecurity(MessagePackSecurity.UntrustedData);
            })
            .AddHubInstrumentation();

        services.AddScoped<INotificationBroadcaster, NotificationBroadcaster>();

        return services;
    }
}
