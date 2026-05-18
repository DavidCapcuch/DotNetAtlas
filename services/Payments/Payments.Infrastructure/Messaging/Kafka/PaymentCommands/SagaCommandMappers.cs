using AppAuthorizePaymentCommand = Payments.Application.Transactions.AuthorizePayment.AuthorizePaymentCommand;
using AppCapturePaymentCommand = Payments.Application.Transactions.CapturePayment.CapturePaymentCommand;
using AppRequestRefundCommand = Payments.Application.Transactions.RequestRefund.RequestRefundCommand;
using AppVoidPaymentCommand = Payments.Application.Transactions.VoidPayment.VoidPaymentCommand;
using AvroAuthorizePaymentCommand = Payments.Transactions.AuthorizePaymentCommand;
using AvroCapturePaymentCommand = Payments.Transactions.CapturePaymentCommand;
using AvroRequestRefundCommand = Payments.Transactions.RequestRefundCommand;
using AvroVoidPaymentCommand = Payments.Transactions.VoidPaymentCommand;

namespace Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Translates saga-issued Avro commands on <c>payments.commands</c> to the application-layer
/// command DTOs. Pure functions, no DI, no side-effects — simple mapping is clearer than a
/// Mapperly config here because the shape differences (decimal AvroDecimal → C# decimal,
/// Avro <c>UserId</c> → App <c>BuyerId</c> rename, PaymentId derivation from
/// <c>CorrelationId</c> per the one-payment-per-saga rule) are all explicit.
/// </summary>
internal static class SagaCommandMappers
{
    /// <summary>
    /// Maps <see cref="AvroAuthorizePaymentCommand"/> to the application-layer
    /// <see cref="AppAuthorizePaymentCommand"/>. Field renames: <c>UserId</c> → <c>BuyerId</c>;
    /// PaymentId derived from <c>CorrelationId</c> (one-payment-per-saga assumption — see
    /// <c>docs/bc-design/payments.md § 4</c>).
    /// </summary>
    internal static AppAuthorizePaymentCommand ToAppCommand(this AvroAuthorizePaymentCommand avro) =>
        new()
        {
            PaymentId = avro.CorrelationId,
            CorrelationId = avro.CorrelationId,
            OrderId = avro.OrderId,
            BuyerId = avro.UserId,
            Amount = (decimal)avro.Amount,
            Currency = avro.Currency,
            PaymentMethodId = avro.PaymentMethodId.ToString(),
        };

    /// <summary>
    /// Maps <see cref="AvroCapturePaymentCommand"/> to the application-layer
    /// <see cref="AppCapturePaymentCommand"/>. PaymentId derived from <c>CorrelationId</c>;
    /// <c>AuthorizationId</c> propagated for handler-side validation against the stored
    /// <c>GatewayTransactionId</c> (H-8 closeout).
    /// </summary>
    internal static AppCapturePaymentCommand ToAppCommand(this AvroCapturePaymentCommand avro) =>
        new()
        {
            PaymentId = avro.CorrelationId,
            CorrelationId = avro.CorrelationId,
            AuthorizationId = avro.AuthorizationId,
        };

    /// <summary>
    /// Maps <see cref="AvroVoidPaymentCommand"/> to the application-layer
    /// <see cref="AppVoidPaymentCommand"/>. PaymentId derived from <c>CorrelationId</c>;
    /// <c>AuthorizationId</c> propagated for handler-side validation (H-8 closeout).
    /// </summary>
    internal static AppVoidPaymentCommand ToAppCommand(this AvroVoidPaymentCommand avro) =>
        new()
        {
            PaymentId = avro.CorrelationId,
            CorrelationId = avro.CorrelationId,
            AuthorizationId = avro.AuthorizationId,
        };

    /// <summary>
    /// Maps <see cref="AvroRequestRefundCommand"/> to the application-layer
    /// <see cref="AppRequestRefundCommand"/>. PaymentId comes directly from the wire
    /// <c>PaymentTransactionId</c> field (refund explicitly references an existing transaction).
    /// </summary>
    internal static AppRequestRefundCommand ToAppCommand(this AvroRequestRefundCommand avro) =>
        new()
        {
            PaymentId = avro.PaymentTransactionId,
            CorrelationId = avro.CorrelationId,
            Reason = avro.Reason,
        };
}
