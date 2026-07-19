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
/// Post-ADR-0029/0030 the saga key is the <c>OrderId</c>, read from each command's own wire
/// payload field: Authorize / Capture / Void resolve their aggregate by <c>OrderId</c>,
/// RequestRefund by the wire <c>PaymentTransactionId</c>.
/// </remarks>
internal static class SagaCommandMappers
{
    /// <summary>
    /// Maps <see cref="AvroAuthorizePaymentCommand"/> to the application-layer
    /// <see cref="AppAuthorizePaymentCommand"/>. Field renames: <c>UserId</c> → <c>BuyerId</c>;
    /// PaymentId comes from the saga-issued <c>PaymentTransactionId</c> Avro field (cross-cutting
    /// #255): a distinct v7 id, not the saga key, so the Payments aggregate PK stays a genuine
    /// UUID v7. One-payment-per-order stays enforced by the unique index on
    /// <c>payment_transactions.order_id</c> (ADR-0029).
    /// </summary>
    internal static AppAuthorizePaymentCommand ToAppCommand(this AvroAuthorizePaymentCommand avro) =>
        new()
        {
            PaymentId = avro.PaymentTransactionId,
            OrderId = avro.OrderId,
            BuyerId = avro.UserId,
            Amount = (decimal)avro.Amount,
            Currency = avro.Currency,
            // AvroAuthorizePaymentCommand.PaymentMethodId is a plain `string` (not a
            // logicalType:uuid `Guid`), so no `.ToString()` is needed.
            PaymentMethodId = avro.PaymentMethodId,
            IdempotencyKey = avro.IdempotencyKey,
        };

    /// <summary>
    /// Maps <see cref="AvroCapturePaymentCommand"/> to the application-layer
    /// <see cref="AppCapturePaymentCommand"/>. <c>OrderId</c> comes from the wire payload field
    /// (ADR-0029/0030) so the handler resolves the aggregate via the unique <c>order_id</c> index;
    /// <c>AuthorizationId</c> propagated for handler-side validation against the stored
    /// <c>GatewayTransactionId</c> (H-8).
    /// </summary>
    internal static AppCapturePaymentCommand ToAppCommand(this AvroCapturePaymentCommand avro) =>
        new()
        {
            OrderId = avro.OrderId,
            AuthorizationId = avro.AuthorizationId,
        };

    /// <summary>
    /// Maps <see cref="AvroVoidPaymentCommand"/> to the application-layer
    /// <see cref="AppVoidPaymentCommand"/>. <c>OrderId</c> comes from the wire payload field
    /// (ADR-0029/0030); <c>AuthorizationId</c> propagated for handler-side validation
    /// (H-8); <c>Reason</c> propagated for aggregate persistence + outbound audit
    /// (H-5).
    /// </summary>
    internal static AppVoidPaymentCommand ToAppCommand(this AvroVoidPaymentCommand avro) =>
        new()
        {
            OrderId = avro.OrderId,
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
