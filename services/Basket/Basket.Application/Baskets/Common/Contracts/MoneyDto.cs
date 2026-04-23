namespace Basket.Application.Baskets.Common.Contracts;

/// <summary>
/// Response-side money DTO — returned by <c>GetBasketByUserIdQuery</c> so the API
/// layer doesn't leak <c>Platform.SharedKernel.ValueObjects.Money</c> onto the wire.
/// </summary>
/// <param name="Amount">Monetary amount (decimal 19,4 — matches the Avro precision).</param>
/// <param name="Currency">ISO 4217 currency code (e.g., <c>"USD"</c>, <c>"CZK"</c>).</param>
public sealed record MoneyDto(decimal Amount, string Currency);
