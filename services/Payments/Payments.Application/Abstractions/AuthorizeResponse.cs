using Payments.Domain.Transactions.ValueObjects;

namespace Payments.Application.Abstractions;

/// <summary>
/// Successful response from <see cref="IPaymentGateway.AuthorizeAsync"/>. Carries the
/// gateway-issued transaction id (immutable once set per invariant I-4), the raw response code
/// stored verbatim on the aggregate for forensics, and the gateway-stated authorization expiry
/// window. The expiry MUST be sourced from the gateway response so the wire-shape
/// <c>PaymentAuthorizedEvent.ExpiresAtUtc</c> is truthful rather than a synthesized placeholder
/// (H-6).
/// </summary>
/// <param name="GatewayTransactionId">Gateway-issued token (non-empty).</param>
/// <param name="ResponseCode">Raw gateway response.</param>
/// <param name="ExpiresAtUtc">UTC instant at which the authorization expires. Captured by the
/// adapter from the gateway; v1 stub returns <c>now + 7 days</c>.</param>
public sealed record AuthorizeResponse(string GatewayTransactionId, GatewayResponseCode ResponseCode, DateTimeOffset ExpiresAtUtc);
