using Payments.Domain.Transactions.ValueObjects;

namespace Payments.Application.Abstractions;

/// <summary>
/// Successful response from <see cref="IPaymentGateway.CaptureAsync"/>. Echoes back the
/// gateway transaction id supplied on the call so the handler can pass it to
/// <see cref="Payments.Domain.Transactions.PaymentTransaction.Capture"/> (which enforces
/// the append-only guard, invariant I-4).
/// </summary>
/// <param name="GatewayTransactionId">Same token supplied to the call.</param>
/// <param name="ResponseCode">Raw gateway response.</param>
public sealed record CaptureResponse(string GatewayTransactionId, GatewayResponseCode ResponseCode);
