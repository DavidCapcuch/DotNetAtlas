using Payments.Domain.Transactions.ValueObjects;

namespace Payments.Application.Abstractions;

/// <summary>
/// Successful response from <see cref="IPaymentGateway.RefundAsync"/>. The aggregate already
/// holds the <c>GatewayTransactionId</c> from the prior authorize/capture, so the response
/// only carries the raw gateway response code.
/// </summary>
/// <param name="ResponseCode">Raw gateway response.</param>
public sealed record RefundResponse(GatewayResponseCode ResponseCode);
