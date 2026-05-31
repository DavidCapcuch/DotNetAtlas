using System.Text;
using KafkaFlow;
using Microsoft.Extensions.Options;
using Payments.Transactions;
using Platform.Messaging.Abstractions;
using Weather.Alerts;
using Weather.Application.Common.Messaging;

namespace Weather.Infrastructure.Messaging.Kafka.Dev;

/// <summary>
/// Kafka producer for dev/testing commands that simulate saga orchestrators.
/// Used by dev endpoints to publish commands that would normally come from
/// the Purchase Saga or Extension Saga.
/// </summary>
public class DevEventsKafkaProducer
{
    private readonly IMessageProducer<DevEventsKafkaProducer> _producer;
    private readonly string _weatherAlertSubscriptionsCommandsTopic;
    private readonly string _weatherAlertSubscriptionsTopic;
    private readonly string _paymentCommandsTopic;
    private readonly string _paymentsTopic;

    public DevEventsKafkaProducer(
        IMessageProducer<DevEventsKafkaProducer> producer,
        IOptions<TopicsOptions> topicOptions)
    {
        _producer = producer;
        _weatherAlertSubscriptionsCommandsTopic = topicOptions.Value.WeatherAlertSubscriptionsCommands;
        _weatherAlertSubscriptionsTopic = topicOptions.Value.WeatherAlertSubscriptions;
        _paymentCommandsTopic = topicOptions.Value.PaymentCommands;
        _paymentsTopic = topicOptions.Value.Payments;
    }

    /// <summary>
    /// Publishes an ActivateSubscriptionCommand to simulate the Purchase Saga.
    /// </summary>
    public Task PublishActivateSubscriptionCommandAsync(ActivateAlertSubscriptionCommand command)
    {
        return _producer.ProduceAsync(
            _weatherAlertSubscriptionsCommandsTopic, command.UserId.ToString(), command);
    }

    /// <summary>
    /// Publishes an ExtendSubscriptionCommand to simulate the Extension Saga.
    /// </summary>
    public Task PublishExtendSubscriptionCommandAsync(ExtendAlertSubscriptionCommand command)
    {
        return _producer
            .ProduceAsync(_weatherAlertSubscriptionsCommandsTopic, command.UserId.ToString(), command);
    }

    /// <summary>
    /// Publishes an ExtendSubscriptionCommand with a specific message ID.
    /// Used for testing idempotency by simulating Kafka redeliveries with the same message ID.
    /// </summary>
    public Task PublishExtendSubscriptionCommandWithMessageIdAsync(
        ExtendAlertSubscriptionCommand command,
        Guid messageId)
    {
        var headers = new MessageHeaders
        {
            {
                MessageHeaderKeys.MessageId, Encoding.UTF8.GetBytes(messageId.ToString())
            }
        };

        return _producer
            .ProduceAsync(
                _weatherAlertSubscriptionsCommandsTopic,
                command.UserId.ToString(),
                command,
                headers);
    }

    // Weather Alerts Events
    public Task PublishSubscriptionActivatedEventAsync(
        AlertSubscriptionActivatedEvent eventAlertSubscriptionActivatedEvent)
    {
        return _producer.ProduceAsync(
            _weatherAlertSubscriptionsTopic, eventAlertSubscriptionActivatedEvent.UserId.ToString(),
            eventAlertSubscriptionActivatedEvent);
    }

    public Task PublishSubscriptionActivationFailedEventAsync(
        AlertSubscriptionActivationFailedEvent eventAlertSubscriptionActivationFailedEvent)
    {
        return _producer.ProduceAsync(
            _weatherAlertSubscriptionsTopic, eventAlertSubscriptionActivationFailedEvent.UserId.ToString(),
            eventAlertSubscriptionActivationFailedEvent);
    }

    public Task PublishSubscriptionExtendedEventAsync(
        AlertSubscriptionExtendedEvent eventAlertSubscriptionExtendedEvent)
    {
        return _producer.ProduceAsync(
            _weatherAlertSubscriptionsTopic, eventAlertSubscriptionExtendedEvent.UserId.ToString(),
            eventAlertSubscriptionExtendedEvent);
    }

    public Task PublishSubscriptionExtensionActivationFailedEventAsync(
        AlertSubscriptionExtensionActivationFailedEvent eventAlertSubscriptionExtensionActivationFailedEvent)
    {
        return _producer.ProduceAsync(
            _weatherAlertSubscriptionsTopic, eventAlertSubscriptionExtensionActivationFailedEvent.UserId.ToString(),
            eventAlertSubscriptionExtensionActivationFailedEvent);
    }

    // Payment Commands
    public Task PublishAuthorizePaymentCommandAsync(AuthorizePaymentCommand commandAuthorizePayment)
    {
        return _producer.ProduceAsync(
            _paymentCommandsTopic, commandAuthorizePayment.UserId.ToString(), commandAuthorizePayment);
    }

    public Task PublishCapturePaymentCommandAsync(CapturePaymentCommand commandCapturePayment)
    {
        return _producer.ProduceAsync(
            _paymentCommandsTopic, commandCapturePayment.UserId.ToString(), commandCapturePayment);
    }

    public Task PublishRequestRefundCommandAsync(RequestRefundCommand commandRequestRefund)
    {
        return _producer.ProduceAsync(
            _paymentCommandsTopic, commandRequestRefund.UserId.ToString(), commandRequestRefund);
    }

    public Task PublishVoidPaymentCommandAsync(VoidPaymentCommand commandVoidPayment)
    {
        return _producer.ProduceAsync(
            _paymentCommandsTopic, commandVoidPayment.UserId.ToString(), commandVoidPayment);
    }

    // Payment Events
    public Task PublishPaymentAuthorizationFailedEventAsync(
        PaymentAuthorizationFailedEvent eventPaymentAuthorizationFailed)
    {
        return _producer.ProduceAsync(
            _paymentsTopic, eventPaymentAuthorizationFailed.UserId.ToString(),
            eventPaymentAuthorizationFailed);
    }

    public Task PublishPaymentAuthorizedEventAsync(PaymentAuthorizedEvent eventPaymentAuthorized)
    {
        return _producer.ProduceAsync(
            _paymentsTopic, eventPaymentAuthorized.UserId.ToString(), eventPaymentAuthorized);
    }

    public Task PublishPaymentCapturedEventAsync(PaymentCapturedEvent eventPaymentCaptured)
    {
        return _producer.ProduceAsync(
            _paymentsTopic, eventPaymentCaptured.PaymentTransactionId.ToString(), eventPaymentCaptured);
    }

    public Task PublishPaymentCaptureFailedEventAsync(PaymentCaptureFailedEvent eventPaymentCaptureFailed)
    {
        return _producer.ProduceAsync(
            _paymentsTopic, eventPaymentCaptureFailed.UserId.ToString(), eventPaymentCaptureFailed);
    }

    public Task PublishPaymentCompletedEventAsync(PaymentCompletedEvent eventPaymentCompleted)
    {
        return _producer.ProduceAsync(
            _paymentsTopic, eventPaymentCompleted.PaymentTransactionId.ToString(), eventPaymentCompleted);
    }

    public Task PublishPaymentFailedEventAsync(PaymentFailedEvent eventPaymentFailed)
    {
        return _producer.ProduceAsync(
            _paymentsTopic, eventPaymentFailed.UserId.ToString(), eventPaymentFailed);
    }

    public Task PublishPaymentRefundedEventAsync(PaymentRefundedEvent eventPaymentRefunded)
    {
        return _producer.ProduceAsync(
            _paymentsTopic, eventPaymentRefunded.PaymentTransactionId.ToString(), eventPaymentRefunded);
    }

    public Task PublishRequestPaymentCommandAsync(RequestPaymentCommand requestPaymentCommand)
    {
        // ADR-0023: renamed from PaymentRequestedEvent and moved to payments.payment-commands
        // (was previously published on payments.transactions). The Kafka key remains UserId-derived
        // for dev-tooling continuity; the Checkout saga keys real production traffic by CorrelationId.
        return _producer.ProduceAsync(
            _paymentCommandsTopic, requestPaymentCommand.UserId.ToString(), requestPaymentCommand);
    }

    public Task PublishPaymentVoidedEventAsync(PaymentVoidedEvent eventPaymentVoided)
    {
        return _producer.ProduceAsync(
            _paymentsTopic, eventPaymentVoided.UserId.ToString(), eventPaymentVoided);
    }
}
