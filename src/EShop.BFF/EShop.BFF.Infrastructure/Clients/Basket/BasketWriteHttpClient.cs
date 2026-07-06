using System.Net;
using System.Net.Http.Json;
using EShop.BFF.Infrastructure.Clients.Common;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>
/// HTTP adapter for Basket's item-mutation surface (bff.md § 3.6). A thin verbatim forwarder: it relays
/// Basket's verdict (any status &lt; 500 except 401/403, which reject the exchanged token — infra, not the
/// buyer) as a <see cref="BasketWriteVerdict"/> carrying Basket's own body, and composes no response of its
/// own. The token exchange (<c>basket.write</c>) + resilience are attached by
/// <see cref="BasketWriteClientDependencyInjection"/>.
/// </summary>
internal sealed class BasketWriteHttpClient : IBasketWriteClient
{
    /// <summary>Header carrying the client idempotency token; forwarded unchanged to Basket's <c>.Idempotency()</c>.</summary>
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly HttpClient _http;
    private readonly ILogger<BasketWriteHttpClient> _logger;

    public BasketWriteHttpClient(HttpClient http, ILogger<BasketWriteHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<Result<BasketWriteVerdict>> AddItemAsync(
        AddItemDto item, string? idempotencyKey, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/basket/items")
        {
            Content = JsonContent.Create(item, options: UpstreamJson.Web),
        };

        // The BFF owns no idempotency here — it forwards the caller's key unchanged (bff.md § 3.6); Basket's
        // .Idempotency() owns the replay. An absent key surfaces as Basket's own 400, relayed verbatim.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
        }

        return ForwardAsync(request, ct);
    }

    public Task<Result<BasketWriteVerdict>> ChangeItemQuantityAsync(
        Guid productId, int quantity, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"api/v1/basket/items/{productId}/quantity")
        {
            Content = JsonContent.Create(new ChangeItemQuantityDto(quantity), options: UpstreamJson.Web),
        };

        return ForwardAsync(request, ct);
    }

    public Task<Result<BasketWriteVerdict>> RemoveItemAsync(Guid productId, CancellationToken ct) =>
        ForwardAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/v1/basket/items/{productId}"), ct);

    public Task<Result<BasketWriteVerdict>> ClearAsync(CancellationToken ct) =>
        ForwardAsync(new HttpRequestMessage(HttpMethod.Delete, "api/v1/basket/items"), ct);

    /// <summary>
    /// Sends a prepared mutation request and classifies the outcome (bff.md § 3.6): Basket's own verdict
    /// (any status &lt; 500 except 401/403) is relayed verbatim as <c>Result.Ok(status)</c>; an unreachable
    /// Basket (≥ 500, 401/403 exchanged-token rejection, transport failure, timeout, or circuit-open)
    /// becomes a <see cref="ServiceUnavailableError"/> the endpoint maps to 503 — Basket's internals never
    /// leak.
    /// </summary>
    private async Task<Result<BasketWriteVerdict>> ForwardAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            try
            {
                using var response = await _http.SendAsync(request, ct);

                if ((int)response.StatusCode >= 500
                    || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // 401/403 here rejects the *exchanged service token* (broken exchange infra — audience
                    // mapper, stale JWKS), not the buyer's credential: the BFF already authenticated the
                    // buyer. Relaying it would force-log a valid user out, so it is shielded like a 5xx.
                    _logger.LogError("Basket mutation returned {StatusCode}", (int)response.StatusCode);
                    return Result.Fail<BasketWriteVerdict>(
                        BasketClientErrors.Unavailable($"HTTP {(int)response.StatusCode}"));
                }

                // Relay the verdict verbatim — status plus, when Basket wrote one (RFC 9457 problem details
                // on a decline), its body + content type. A 204 simply has no content.
                var body = await response.Content.ReadAsStringAsync(ct);
                return Result.Ok(string.IsNullOrEmpty(body)
                    ? new BasketWriteVerdict(response.StatusCode)
                    : new BasketWriteVerdict(
                        response.StatusCode, body, response.Content.Headers.ContentType?.ToString()));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // caller cancellation — propagate unchanged
            }
            catch (Exception ex)
                when (ex is HttpRequestException
                    or TaskCanceledException
                    or TimeoutException
                    or Polly.ExecutionRejectedException)
            {
                _logger.LogError(ex, "Basket mutation call failed");
                return Result.Fail<BasketWriteVerdict>(BasketClientErrors.Unavailable(ex.GetType().Name));
            }
        }
    }
}
