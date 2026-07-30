namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// Basket's wire-level representation of Catalog's <c>MoneyDto</c> (decimal <c>Amount</c>,
/// ISO&#xa0;4217 <c>Currency</c> string). Deliberately shared by both Catalog routes where their
/// product records are not: how money is represented is a service-wide decision with no
/// route-specific reason to change — the inbound mirror of ADR-0037's <c>MoneyDto</c> ruling. Never
/// escapes this assembly per basket.md &#xa7; 9.3.
/// </summary>
/// <remarks>
/// Positional because the type is trivially flat — the shape ADR-0037 reserves positional records
/// for, applied here by mirroring rather than by that ADR's jurisdiction, which covers Catalog's
/// published contracts and not this inbound copy of them. That is what makes
/// <c>RespectRequiredConstructorParameters</c> load-bearing rather than ornamental in
/// <c>ProductCatalogHttpAdapter</c>'s serializer options: without it, a <c>price</c> object missing
/// either member binds to <c>default</c> instead of throwing.
/// </remarks>
internal sealed record CatalogPriceDto(decimal Amount, string Currency);
