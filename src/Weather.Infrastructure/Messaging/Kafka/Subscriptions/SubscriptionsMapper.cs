using FluentResults;
using Platform.SharedKernel.Errors;
using Riok.Mapperly.Abstractions;
using Weather.Alerts;
using AppExtendCommand = Weather.Application.WeatherAlerts.ExtendSubscription.ExtendSubscriptionCommand;
using AppPurchaseCommand = Weather.Application.WeatherAlerts.PurchaseSubscription.PurchaseSubscriptionCommand;

namespace Weather.Infrastructure.Messaging.Kafka.Subscriptions;

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
    [MapProperty(nameof(ActivateAlertSubscriptionCommand.Tier), nameof(AppPurchaseCommand.Tier),
        Use = nameof(MapToDomainSubscriptionTier))]
    [MapProperty(nameof(ActivateAlertSubscriptionCommand.RequestedAtUtc), nameof(AppPurchaseCommand.OccurredOnUtc))]
    public static partial AppPurchaseCommand ToPurchaseSubscriptionCommand(this ActivateAlertSubscriptionCommand source);

    /// <summary>
    /// Maps ExtendSubscriptionCommand (Avro) to ExtendSubscriptionCommand (Application).
    /// </summary>
    [MapProperty(nameof(ExtendAlertSubscriptionCommand.DurationDays), nameof(AppExtendCommand.DurationExtendedDays))]
    [MapProperty(nameof(ExtendAlertSubscriptionCommand.RequestedAtUtc), nameof(AppExtendCommand.OccurredOnUtc))]
    public static partial AppExtendCommand ToExtendSubscriptionCommand(this ExtendAlertSubscriptionCommand source);

    /// <summary>
    /// Creates SubscriptionActivationFailedEvent from the original command when activation fails.
    /// </summary>
    [MapProperty(nameof(ActivateAlertSubscriptionCommand.Tier), nameof(AlertSubscriptionActivationFailedEvent.RequestedTier))]
    [MapProperty(nameof(ActivateAlertSubscriptionCommand.DurationDays), nameof(AlertSubscriptionActivationFailedEvent.RequestedDurationDays))]
    [MapProperty(nameof(ActivateAlertSubscriptionCommand.CorrelationId), nameof(AlertSubscriptionActivationFailedEvent.CorrelationId))]
    public static partial AlertSubscriptionActivationFailedEvent ToSubscriptionActivationFailedEvent(
        this ActivateAlertSubscriptionCommand source,
        IList<ErrorDetails> errors,
        DateTime occurredOnUtc);

    /// <summary>
    /// Creates SubscriptionExtensionActivationFailedEvent from the original command when extension fails.
    /// </summary>
    [MapProperty(nameof(ExtendAlertSubscriptionCommand.DurationDays), nameof(AlertSubscriptionExtensionActivationFailedEvent.RequestedDurationExtendedDays))]
    public static partial AlertSubscriptionExtensionActivationFailedEvent ToSubscriptionExtensionActivationFailedEvent(
        this ExtendAlertSubscriptionCommand source,
        IList<ErrorDetails> errors,
        DateTime occurredOnUtc);

    [UserMapping]
    private static Weather.Domain.Alerts.ValueObjects.SubscriptionTier MapToDomainSubscriptionTier(
        SubscriptionTier source) =>
        source switch
        {
            SubscriptionTier.Pro => Domain.Alerts.ValueObjects.SubscriptionTier.Pro,
            SubscriptionTier.Ultra => Domain.Alerts.ValueObjects.SubscriptionTier.Ultra,
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
