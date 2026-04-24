using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Basket.Domain.Baskets.ValueObjects;
using Basket.Infrastructure.ExternalServices.Catalog;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.Errors;

namespace Basket.UnitTests.Baskets.Infrastructure.ExternalServices.Catalog;

public class ProductCatalogHttpAdapterTests
{
    private static readonly Uri BaseAddress = new("http://catalog.local");
    private static readonly DateTimeOffset Now = new(2026, 04, 24, 10, 00, 00, TimeSpan.Zero);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly FakeTimeProvider _time = new(Now);

    private ProductCatalogHttpAdapter CreateSut(StubHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = BaseAddress };
        return new ProductCatalogHttpAdapter(http, _time, NullLogger<ProductCatalogHttpAdapter>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body)
    {
        var json = JsonSerializer.Serialize(body, WireJson);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task GetProductSnapshot_WhenHttp200_ReturnsSnapshotWithMappedFields()
    {
        var productId = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { productId, sku = "SKU-A", name = "Widget", price = new { amount = 9.99m, currency = "USD" } })));

        var result = await CreateSut(handler).GetProductSnapshotAsync(
            productId,
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var snapshot = result.Value;
            snapshot.Sku.Should().Be("SKU-A");
            snapshot.Name.Should().Be("Widget");
            snapshot.Price.Amount.Should().Be(9.99m);
            snapshot.Price.Currency.Name.Should().Be("USD");
            snapshot.CapturedAtUtc.Should().Be(Now);
        }
    }

    [Fact]
    public async Task GetProductSnapshot_WhenHttp404_ReturnsProductNotFound()
    {
        var productId = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await CreateSut(handler).GetProductSnapshotAsync(
            productId,
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<ValidationError>(e => e.ErrorCode == "Basket.ProductNotFound")
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetProductSnapshot_WhenHttp500_ReturnsCatalogUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        AssertCatalogUnavailable(result);
    }

    [Fact]
    public async Task GetProductSnapshot_WhenHttp400_ReturnsCatalogUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        AssertCatalogUnavailable(result);
    }

    [Fact]
    public async Task GetProductSnapshot_WhenHttpClientTimeout_ReturnsCatalogUnavailable()
    {
        // HttpClient.Timeout firing surfaces as TaskCanceledException with an
        // inner TimeoutException; the caller token is NOT cancelled.
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new TaskCanceledException("simulated http timeout", new TimeoutException()));

        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        AssertCatalogUnavailable(result);
    }

    [Fact]
    public async Task GetProductSnapshot_WhenCallerCancels_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHttpMessageHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var sut = CreateSut(handler);

        var act = async () => await sut.GetProductSnapshotAsync(Guid.CreateVersion7(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetProductSnapshot_WhenHttpRequestException_ReturnsCatalogUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("simulated DNS failure"));

        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        AssertCatalogUnavailable(result);
    }

    [Fact]
    public async Task GetProductSnapshot_WhenMalformedJson_ReturnsCatalogUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        }));

        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        AssertCatalogUnavailable(result);
    }

    [Fact]
    public async Task GetMany_WhenEmptyInput_SkipsHttpCallAndReturnsEmptyList()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HttpClient must not be called with empty input."));

        var result = await CreateSut(handler).GetManyAsync(
            Array.Empty<Guid>(),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().BeEmpty();
            handler.CallCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task GetMany_WhenHappyPath_ReturnsPairsForAllProducts()
    {
        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();
        var id3 = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new
            {
                products = new[]
                {
                    new { productId = id1, sku = "A", name = "Alpha", price = new { amount = 1m, currency = "USD" } },
                    new { productId = id2, sku = "B", name = "Beta", price = new { amount = 2m, currency = "USD" } },
                    new { productId = id3, sku = "C", name = "Gamma", price = new { amount = 3m, currency = "USD" } },
                },
                missingProductIds = Array.Empty<Guid>(),
            })));

        var result = await CreateSut(handler).GetManyAsync(
            new[] { id1, id2, id3 },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().HaveCount(3);
            result.Value.Select(p => p.ProductId).Should().BeEquivalentTo(new[] { id1, id2, id3 });
            result.Value.Single(p => p.ProductId == id1).Snapshot.Sku.Should().Be("A");
        }
    }

    [Fact]
    public async Task GetMany_WhenPartialMiss_DropsMissingIdsSilently()
    {
        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();
        var id3 = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new
            {
                products = new[]
                {
                    new { productId = id1, sku = "A", name = "Alpha", price = new { amount = 1m, currency = "USD" } },
                    new { productId = id2, sku = "B", name = "Beta", price = new { amount = 2m, currency = "USD" } },
                },
                missingProductIds = new[] { id3 },
            })));

        var result = await CreateSut(handler).GetManyAsync(
            new[] { id1, id2, id3 },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().HaveCount(2);
            result.Value.Select(p => p.ProductId).Should().BeEquivalentTo(new[] { id1, id2 });
            result.Value.Select(p => p.ProductId).Should().NotContain(id3);
        }
    }

    [Fact]
    public async Task GetMany_WhenHttp500_ReturnsCatalogUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await CreateSut(handler).GetManyAsync(
            new[] { Guid.CreateVersion7(), Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        AssertCatalogUnavailable(result);
    }

    [Fact]
    public async Task GetMany_JoinsIdsAsSingleCommaSeparatedQuery()
    {
        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();
        var id3 = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { products = Array.Empty<object>(), missingProductIds = new[] { id1, id2, id3 } })));

        _ = await CreateSut(handler).GetManyAsync(
            new[] { id1, id2, id3 },
            TestContext.Current.CancellationToken);

        handler.LastRequestPathAndQuery.Should().Be(
            $"/api/v1/catalog/products/by-ids?ids={id1:D},{id2:D},{id3:D}");
    }

    [Fact]
    public async Task GetMany_DeduplicatesInputIds()
    {
        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { products = Array.Empty<object>(), missingProductIds = new[] { id1, id2 } })));

        _ = await CreateSut(handler).GetManyAsync(
            new[] { id1, id1, id2 },
            TestContext.Current.CancellationToken);

        handler.LastRequestPathAndQuery.Should().Be(
            $"/api/v1/catalog/products/by-ids?ids={id1:D},{id2:D}");
    }

    private static void AssertCatalogUnavailable<T>(Result<T> result)
    {
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<ValidationError>(e => e.ErrorCode == "Basket.CatalogUnavailable")
                .Should().BeTrue();
        }
    }
}
