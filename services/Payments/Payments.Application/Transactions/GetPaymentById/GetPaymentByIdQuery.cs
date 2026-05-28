using Platform.CQRS;

namespace Payments.Application.Transactions.GetPaymentById;

/// <summary>
/// Admin lookup by aggregate id. Authorization (<c>AuthPolicies.PaymentsAdmin</c>) is enforced
/// at the endpoint level.
/// </summary>
public sealed record GetPaymentByIdQuery(Guid PaymentId) : IQuery<GetPaymentByIdResponse>;
