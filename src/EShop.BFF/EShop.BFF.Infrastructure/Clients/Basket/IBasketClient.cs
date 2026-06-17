using FluentResults;

namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>
/// Typed client for Basket's buyer-scoped read (bff.md § 4.2). Reached via the RFC 8693 token exchange on
/// the <c>basket.read</c> scope so the buyer <c>sub</c> is preserved and Basket resolves the right buyer
/// (ADR-0010 amendment 2026-06-06). Returns <see cref="Result{T}"/> so the endpoint distinguishes "no
/// basket yet" (404 → empty page) from "Basket unavailable" (transport / 5xx → fail-safe / 503).
/// </summary>
internal interface IBasketClient
{
    Task<Result<BasketDto>> GetBasketAsync(CancellationToken ct);
}
