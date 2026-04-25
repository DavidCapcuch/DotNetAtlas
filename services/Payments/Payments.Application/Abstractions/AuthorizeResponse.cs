using Payments.Domain.Transactions.ValueObjects;

namespace Payments.Application.Abstractions;

/// <summary>
/// Successful response from <see cref="IPaymentGateway.AuthorizeAsync"/>. Carries the
/// gateway-issued transaction id (immutable once set per invariant I-4) and the raw response
/// code stored verbatim on the aggregate for forensics.
/// </summary>
/// <param name="GatewayTransactionId">Gateway-issued token (non-empty).</param>
/// <param name="ResponseCode">Raw gateway response.</param>
public sealed record AuthorizeResponse(string GatewayTransactionId, GatewayResponseCode ResponseCode);
