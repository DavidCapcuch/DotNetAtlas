using DotNetAtlas.Application.WeatherAlerts.ExtendSubscription;
using DotNetAtlas.Application.WeatherAlerts.PurchaseSubscription;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults;
using Riok.Mapperly.Abstractions;
using Weather.Alerts;
using ActivateCommand = Weather.Alerts.ActivateSubscriptionCommand;
using AppExtendCommand = DotNetAtlas.Application.WeatherAlerts.ExtendSubscription.ExtendSubscriptionCommand;
using AppPurchaseCommand = DotNetAtlas.Application.WeatherAlerts.PurchaseSubscription.PurchaseSubscriptionCommand;
using ExtendCommand = Weather.Alerts.ExtendSubscriptionCommand;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.Subscriptions;

/// <summary>
/// Mapper for converting Weather Alerts commands from saga to application commands,
/// and for creating failure integration events for saga compensation.
/// Uses Mapperly source generator for compile-time mapping.
/// </summary>
[Mapper(
    RequiredMappingStrategy = RequiredMappingStrategy.Target,
    EnumMappingStrategy = EnumMappingStrategy.ByName)]
public static partial class SubscriptionsMapper
{
    /// <summary>
    /// Maps ActivateSubscriptionCommand (Avro) to PurchaseSubscriptionCommand (Application).
    /// </summary>
    [MapProperty(nameof(ActivateCommand.Tier), nameof(AppPurchaseCommand.Tier),
        Use = nameof(MapToDomainSubscriptionTier))]
    [MapProperty(nameof(ActivateCommand.RequestedAtUtc), nameof(AppPurchaseCommand.OccurredOnUtc))]
    public static partial AppPurchaseCommand ToPurchaseSubscriptionCommand(this ActivateCommand source);

    /// <summary>
    /// Maps ExtendSubscriptionCommand (Avro) to ExtendSubscriptionCommand (Application).
    /// </summary>
    [MapProperty(nameof(ExtendCommand.DurationDays), nameof(AppExtendCommand.DurationExtendedDays))]
    [MapProperty(nameof(ExtendCommand.RequestedAtUtc), nameof(AppExtendCommand.OccurredOnUtc))]
    public static partial AppExtendCommand ToExtendSubscriptionCommand(this ExtendCommand source);

    /// <summary>
    /// Creates SubscriptionActivationFailedEvent from the original command when activation fails.
    /// </summary>
    [MapProperty(nameof(ActivateCommand.Tier), nameof(SubscriptionActivationFailedEvent.RequestedTier))]
    [MapProperty(nameof(ActivateCommand.DurationDays), nameof(SubscriptionActivationFailedEvent.RequestedDurationDays))]
    [MapProperty(nameof(ActivateCommand.CorrelationId), nameof(SubscriptionActivationFailedEvent.CorrelationId))]
    public static partial SubscriptionActivationFailedEvent ToSubscriptionActivationFailedEvent(
        this ActivateCommand source,
        IList<ErrorDetails> errors,
        DateTime occurredOnUtc);

    /// <summary>
    /// Creates SubscriptionExtensionActivationFailedEvent from the original command when extension fails.
    /// </summary>
    [MapProperty(nameof(ExtendCommand.DurationDays), nameof(SubscriptionExtensionActivationFailedEvent.RequestedDurationExtendedDays))]
    public static partial SubscriptionExtensionActivationFailedEvent ToSubscriptionExtensionActivationFailedEvent(
        this ExtendCommand source,
        IList<ErrorDetails> errors,
        DateTime occurredOnUtc);

    [UserMapping]
    private static Domain.Alerts.ValueObjects.SubscriptionTier MapToDomainSubscriptionTier(
        Weather.Alerts.SubscriptionTier source) =>
        source switch
        {
            Weather.Alerts.SubscriptionTier.Pro => Domain.Alerts.ValueObjects.SubscriptionTier.Pro,
            Weather.Alerts.SubscriptionTier.Ultra => Domain.Alerts.ValueObjects.SubscriptionTier.Ultra,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown SubscriptionTier value.")
        };

    [UserMapping]
    private static DateTimeOffset DateTimeToDateTimeOffset(DateTime dateTime) =>
        new(dateTime, TimeSpan.Zero);

    /// <summary>
    /// Maps FluentResults errors to Avro ErrorDetails for saga compensation events.
    /// Extracts ErrorCode from DomainError or uses type name as fallback.
    /// </summary>
    public static IList<ErrorDetails> ToAvroErrorDetails(this IEnumerable<IError> errors) =>
    [
        .. errors.ToErrorDetails()
            .Select(e => new ErrorDetails
            {
                ErrorCode = e.ErrorCode,
                ErrorMessage = e.ErrorMessage
            })
    ];
}
