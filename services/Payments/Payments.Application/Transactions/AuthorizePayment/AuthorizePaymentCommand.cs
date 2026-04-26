using Platform.CQRS;

namespace Payments.Application.Transactions.AuthorizePayment;

/// <summary>
/// Internal CQRS command that drives the Payments aggregate's first transition. The M5 Kafka
/// consumer translates the wire-shape <c>Payments.Transactions.AuthorizePaymentCommand</c> Avro
/// record into this internal type, deriving <see cref="PaymentId"/> from the saga
/// <see cref="CorrelationId"/> (one-payment-per-saga assumption per the M4 plan Path B). If the
/// aggregate already exists in <c>Requested</c>, the handler authorizes it; otherwise it
/// creates and authorizes in a single step. Returns the canonical aggregate id so the saga
/// can confirm what Payments persisted matches what the saga sent.
/// </summary>
public sealed record AuthorizePaymentCommand : ICommand<Guid>
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string PaymentMethodId { get; init; }
}
