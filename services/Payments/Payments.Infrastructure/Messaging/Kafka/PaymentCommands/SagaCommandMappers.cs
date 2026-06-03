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
/// Translates saga-issued Avro commands on <c>payments.payment-commands</c> to the
/// application-layer command DTOs. Pure functions, no DI, no side-effects.
/// </summary>
/// <remarks>
/// ADR-0008: the saga key is passed in explicitly from the Kafka header rather than read from the
/// Avro payload field — the header is the authoritative source. Post-ADR-0029 that key equals the
/// <c>OrderId</c>; ADR-0030 retires the dedicated correlation id, so Capture / Void resolve their
/// aggregate by <c>OrderId</c> and RequestRefund by the wire <c>PaymentTransactionId</c>.
/// </remarks>
internal static class SagaCommandMappers
{
    /// <summary>
    /// Maps <see cref="AvroAuthorizePaymentCommand"/> to the application-layer
    /// <see cref="AppAuthorizePaymentCommand"/>. Field renames: <c>UserId</c> → <c>BuyerId</c>;
    /// PaymentId comes from the saga-issued <c>PaymentTransactionId</c> Avro field (cross-cutting
    /// wave1-followup #255 — the v1 collapse where PaymentId == CorrelationId was unwound to make
    /// the "v7 PK" guarantee on the Payments aggregate genuine). One-payment-per-saga stays
    /// enforced by the unique index on <c>payment_transactions.correlation_id</c>.
    /// </summary>
    internal static AppAuthorizePaymentCommand ToAppCommand(this AvroAuthorizePaymentCommand avro, Guid correlationId) =>
        new()
        {
            PaymentId = avro.PaymentTransactionId,
            CorrelationId = correlationId,
            OrderId = avro.OrderId,
            BuyerId = avro.UserId,
            Amount = (decimal)avro.Amount,
            Currency = avro.Currency,
            // C-2 closeout: AvroAuthorizePaymentCommand.PaymentMethodId is now `string` (was
            // `Guid` via logicalType:uuid). No `.ToString()` needed.
            PaymentMethodId = avro.PaymentMethodId,
            IdempotencyKey = avro.IdempotencyKey,
        };

    /// <summary>
    /// Maps <see cref="AvroCapturePaymentCommand"/> to the application-layer
    /// <see cref="AppCapturePaymentCommand"/>. <c>OrderId</c> comes from the saga key
    /// (== OrderId per ADR-0029) so the handler resolves the aggregate via the unique
    /// <c>order_id</c> index; <c>AuthorizationId</c> propagated for handler-side validation
    /// against the stored <c>GatewayTransactionId</c> (H-8 closeout).
    /// </summary>
    internal static AppCapturePaymentCommand ToAppCommand(this AvroCapturePaymentCommand avro, Guid orderId) =>
        new()
        {
            OrderId = orderId,
            AuthorizationId = avro.AuthorizationId,
        };

    /// <summary>
    /// Maps <see cref="AvroVoidPaymentCommand"/> to the application-layer
    /// <see cref="AppVoidPaymentCommand"/>. <c>OrderId</c> comes from the saga key
    /// (== OrderId per ADR-0029); <c>AuthorizationId</c> propagated for handler-side validation
    /// (H-8 closeout); <c>Reason</c> propagated for aggregate persistence + outbound audit
    /// (H-5 closeout).
    /// </summary>
    internal static AppVoidPaymentCommand ToAppCommand(this AvroVoidPaymentCommand avro, Guid orderId) =>
        new()
        {
            OrderId = orderId,
            AuthorizationId = avro.AuthorizationId,
            Reason = avro.Reason,
        };

    /// <summary>
    /// Maps <see cref="AvroRequestRefundCommand"/> to the application-layer
    /// <see cref="AppRequestRefundCommand"/>. PaymentId comes directly from the wire
    /// <c>PaymentTransactionId</c> field (refund explicitly references an existing transaction),
    /// so the handler resolves the aggregate by its primary key.
    /// </summary>
    internal static AppRequestRefundCommand ToAppCommand(this AvroRequestRefundCommand avro) =>
        new()
        {
            PaymentId = avro.PaymentTransactionId,
            Reason = avro.Reason,
        };
}
