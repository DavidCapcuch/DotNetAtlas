using Platform.CQRS;

namespace Payments.Application.Transactions.AuthorizePayment;

/// <summary>
/// Internal CQRS command that drives the Payments aggregate's first transition. The Kafka
/// consumer translates the wire-shape <c>Payments.Transactions.AuthorizePaymentCommand</c> Avro
/// record into this internal type, deriving <see cref="PaymentId"/> from the saga-issued
/// <c>PaymentTransactionId</c> Avro field (#255). If the
/// aggregate already exists in <c>Requested</c>, the handler authorizes it; otherwise it
/// creates and authorizes in a single step. Returns the canonical aggregate id so the saga
/// can confirm what Payments persisted matches what the saga sent.
/// </summary>
public sealed record AuthorizePaymentCommand : ICommand<Guid>
{
    public required Guid PaymentId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string PaymentMethodId { get; init; }

    /// <summary>
    /// Saga-issued idempotency key. Threaded through to <c>IPaymentGateway.AuthorizeAsync</c>
    /// so a real PSP adapter (Stripe / Adyen) can forward it as the gateway's
    /// <c>Idempotency-Key</c> header. Even though the Payments-side inbox already dedups Kafka
    /// replays, this gives the gateway-side an independent safety net for the
    /// "SaveChanges fails after gateway succeeded" recovery path.
    /// </summary>
    public required string IdempotencyKey { get; init; }
}
