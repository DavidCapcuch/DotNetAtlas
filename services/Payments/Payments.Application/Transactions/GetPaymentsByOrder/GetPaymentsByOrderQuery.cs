using Platform.CQRS;

namespace Payments.Application.Transactions.GetPaymentsByOrder;

/// <summary>
/// Admin lookup of all payment transactions for a given order. Returns an empty list (not 404)
/// if no payments exist — orders may fail before any payment is requested.
/// </summary>
public sealed record GetPaymentsByOrderQuery(Guid OrderId) : IQuery<GetPaymentsByOrderResponse>;
