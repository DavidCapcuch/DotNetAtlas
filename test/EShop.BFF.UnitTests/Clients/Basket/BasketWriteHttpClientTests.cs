using System.Net;
using System.Text.Json;
using EShop.BFF.Infrastructure.Clients.Basket;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.SharedKernel.Errors;

namespace EShop.BFF.UnitTests.Clients.Basket;

/// <summary>
/// The basket write client (bff.md § 3.6 / § 4.2) is a thin verbatim forwarder: it relays Basket's own
/// verdict (any status &lt; 500 except 401/403, plus any body Basket wrote) as a <c>BasketWriteVerdict</c>
/// and only authors a verdict of its own when Basket is unreachable (≥ 500 / 401-403 exchanged-token
/// rejection / transport / circuit-open → <see cref="ServiceUnavailableError"/>, which the unified
/// <c>SendErrorResponseAsync</c> mapper turns into a 503). These tests pin the request shape (route, method,
/// body, forwarded <c>Idempotency-Key</c>) and that classification.
/// </summary>
public sealed class BasketWriteHttpClientTests
{
    private static readonly Guid ProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AddItemAsync_WhenBasketReturns204_PostsToItemsRouteAndReturnsOk204()
    {
        // Arrange
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var client = CreateClient(handler);

        // Act
        var result = await client.AddItemAsync(
            new AddItemDto(ProductId, 3), idempotencyKey: null, ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Status.Should().Be(HttpStatusCode.NoContent);

            handler.Request!.Method.Should().Be(HttpMethod.Post);
            handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/basket/items");

            using var body = JsonDocument.Parse(handler.RequestBody!);
            body.RootElement.GetProperty("productId").GetGuid().Should().Be(ProductId);
            body.RootElement.GetProperty("quantity").GetInt32().Should().Be(3);
        }
    }

    [Theory]
    [Trait("Category", "boundary")]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task AddItemAsync_WhenBasketRepliesUnder500_RelaysTheVerdictVerbatim(HttpStatusCode status)
    {
        // Arrange — any < 500 is Basket's own verdict, forwarded unchanged.
        var client = CreateClient(new CapturingHandler(status));

        // Act
        var result = await client.AddItemAsync(
            new AddItemDto(ProductId, 1), idempotencyKey: null, ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Status.Should().Be(status);
        }
    }

    [Theory]
    [Trait("Category", "resilience")]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AddItemAsync_WhenBasketReplies5xx_FailsAsServiceUnavailable(HttpStatusCode status)
    {
        // Arrange — an unreachable / erroring Basket is shielded, not leaked (mirrors the read path).
        var client = CreateClient(new CapturingHandler(status));

        // Act
        var result = await client.AddItemAsync(
            new AddItemDto(ProductId, 1), idempotencyKey: null, ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.HasError<ServiceUnavailableError>().Should().BeTrue();
        }
    }

    [Theory]
    [Trait("Category", "resilience")]
    [Trait("Category", "security")]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AddItemAsync_WhenBasketRejectsTheExchangedToken_FailsAsServiceUnavailable(
        HttpStatusCode status)
    {
        // Arrange — a 401/403 from Basket rejects the BFF's *exchanged service token* (broken exchange infra:
        // audience mapper, stale JWKS), not the buyer's credential — the BFF already authenticated the buyer.
        // Relaying it would force-log a valid user out; it is shielded as 503 like any unreachable Basket.
        var client = CreateClient(new CapturingHandler(status));

        // Act
        var result = await client.AddItemAsync(
            new AddItemDto(ProductId, 1), idempotencyKey: null, ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.HasError<ServiceUnavailableError>().Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task AddItemAsync_WhenTransportFails_FailsAsServiceUnavailable()
    {
        // Arrange — a dropped connection / timeout never throws out of the thin forwarder.
        var client = CreateClient(new ThrowingHandler(new HttpRequestException("connection refused")));

        // Act
        var result = await client.AddItemAsync(
            new AddItemDto(ProductId, 1), idempotencyKey: null, ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.IsFailed.Should().BeTrue();
            result.HasError<ServiceUnavailableError>().Should().BeTrue();
        }
    }

    [Fact]
    public async Task AddItemAsync_WhenBasketDeclinesWithProblemDetails_RelaysStatusBodyAndContentType()
    {
        // Arrange — Basket 409s with RFC 9457 problem details (EmptyBasket vs MaxItemsReached carry different
        // UX flows); the forwarder relays the body verbatim, not just the status (bff.md § 3.6).
        const string problemJson = /*lang=json,strict*/
            """{"type":"urn:basket:max-items-reached","title":"Conflict","status":409}""";
        var handler = new CapturingHandler(
            HttpStatusCode.Conflict, responseBody: problemJson, responseContentType: "application/problem+json");
        var client = CreateClient(handler);

        // Act
        var result = await client.AddItemAsync(
            new AddItemDto(ProductId, 1), idempotencyKey: null, ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Status.Should().Be(HttpStatusCode.Conflict);
            result.Value.Body.Should().Be(problemJson);
            result.Value.ContentType.Should().StartWith("application/problem+json");
        }
    }

    [Fact]
    public async Task AddItemAsync_WhenIdempotencyKeyProvided_ForwardsItUnchanged()
    {
        // Arrange
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var client = CreateClient(handler);

        // Act
        await client.AddItemAsync(
            new AddItemDto(ProductId, 1), idempotencyKey: "abc-123", ct: TestContext.Current.CancellationToken);

        // Assert — the BFF owns no idempotency here; Basket's .Idempotency() does (bff.md § 3.6).
        handler.Request!.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().Be("abc-123");
    }

    [Fact]
    public async Task ChangeItemQuantityAsync_WhenBasketReturns204_PutsToQuantityRouteWithNewQuantityBody()
    {
        // Arrange
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var client = CreateClient(handler);

        // Act
        var result = await client.ChangeItemQuantityAsync(
            ProductId, quantity: 5, ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Value.Status.Should().Be(HttpStatusCode.NoContent);
            handler.Request!.Method.Should().Be(HttpMethod.Put);
            handler.Request.RequestUri!.AbsolutePath.Should().Be($"/api/v1/basket/items/{ProductId}/quantity");

            using var body = JsonDocument.Parse(handler.RequestBody!);
            body.RootElement.GetProperty("newQuantity").GetInt32().Should().Be(5);
        }
    }

    [Fact]
    public async Task RemoveItemAsync_WhenBasketReturns204_DeletesTheItemRoute()
    {
        // Arrange
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var client = CreateClient(handler);

        // Act
        var result = await client.RemoveItemAsync(ProductId, ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Value.Status.Should().Be(HttpStatusCode.NoContent);
            handler.Request!.Method.Should().Be(HttpMethod.Delete);
            handler.Request.RequestUri!.AbsolutePath.Should().Be($"/api/v1/basket/items/{ProductId}");
            handler.RequestBody.Should().BeNullOrEmpty();
        }
    }

    [Fact]
    public async Task ClearAsync_WhenBasketReturns204_DeletesTheItemsCollectionRoute()
    {
        // Arrange
        var handler = new CapturingHandler(HttpStatusCode.NoContent);
        var client = CreateClient(handler);

        // Act
        var result = await client.ClearAsync(ct: TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Value.Status.Should().Be(HttpStatusCode.NoContent);
            handler.Request!.Method.Should().Be(HttpMethod.Delete);
            handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/basket/items");
        }
    }

    private static BasketWriteHttpClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://basket.test") },
            NullLogger<BasketWriteHttpClient>.Instance);

    /// <summary>Captures the single outbound request and replies with a fixed status (and optional body).</summary>
    private sealed class CapturingHandler(
        HttpStatusCode statusCode, string? responseBody = null, string? responseContentType = null)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content is not null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(statusCode);
            if (responseBody is not null)
            {
                response.Content = new StringContent(
                    responseBody, System.Text.Encoding.UTF8, responseContentType ?? "application/json");
            }

            return response;
        }
    }

    /// <summary>Simulates a transport failure (dropped connection / timeout) by throwing on send.</summary>
    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => throw exception;
    }
}
