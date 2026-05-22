namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// Private wire-level representation of Catalog's <c>MoneyDto</c> (decimal
/// <c>Amount</c>, ISO&#xa0;4217 <c>Currency</c> string). Co-located with
/// <see cref="CatalogProductResponse"/> because the two share a reason for
/// change — Catalog's transport contract. Never escapes this assembly per
/// basket.md &#xa7; 9.3 (architecture test enforces this DTO shape).
/// </summary>
internal sealed record CatalogPriceDto(decimal Amount, string Currency);
