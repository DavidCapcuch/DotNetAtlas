using Payments.Domain.Transactions.ValueObjects;

namespace Payments.Application.Abstractions;

/// <summary>
/// Successful response from <see cref="IPaymentGateway.VoidAsync"/>. The aggregate already
/// holds the <c>GatewayTransactionId</c> from the prior authorize, so the response only
/// carries the raw gateway response code.
/// </summary>
/// <param name="ResponseCode">Raw gateway response.</param>
public sealed record VoidResponse(GatewayResponseCode ResponseCode);
