using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.BFF.Infrastructure.Clients.Common;
using EShop.BFF.Infrastructure.Common.Observability;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>
/// HTTP adapter for Basket's <c>GET /api/v1/basket</c> (bff.md § 4.2). Distinguishes 404 (no basket yet →
/// gating <c>NotFoundError</c> the endpoint turns into an empty page) from transport failure
/// (<c>ServiceUnavailableError</c> → fail-safe / 503). The buyer is implicit — Basket resolves it from the
/// exchanged token's <c>sub</c>; the BFF sends no user id. The token exchange + resilience are attached by
/// <see cref="BasketClientDependencyInjection"/>.
/// </summary>
internal sealed class BasketHttpClient : IBasketClient
{
    private readonly HttpClient _http;
    private readonly ILogger<BasketHttpClient> _logger;

    public BasketHttpClient(HttpClient http, ILogger<BasketHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Result<BasketDto>> GetBasketAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("api/v1/basket", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No basket yet (lazily created) — the endpoint renders an empty page, not a failure.
                return Result.Fail<BasketDto>(BasketClientErrors.BasketNotFound());
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Basket returned {StatusCode}", (int)response.StatusCode);
                return Result.Fail<BasketDto>(BasketClientErrors.Unavailable($"HTTP {(int)response.StatusCode}"));
            }

            var basket = await response.Content.ReadFromJsonAsync<BasketDto>(UpstreamJson.Web, ct);
            if (basket is null)
            {
                _logger.LogError("Basket returned an empty body");
                return Result.Fail<BasketDto>(BasketClientErrors.Unavailable("empty response body"));
            }

            return Result.Ok(basket);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancellation — propagate unchanged
        }
        catch (Exception ex)
            when (ex is HttpRequestException
                or TaskCanceledException
                or TimeoutException
                or JsonException
                or Polly.ExecutionRejectedException)
        {
            _logger.LogError(ex, "Basket call failed");
            BffMetrics.RecordUnbindablePayload("basket", ex);
            return Result.Fail<BasketDto>(BasketClientErrors.Unavailable(ex.GetType().Name));
        }
    }
}
