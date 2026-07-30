using System.Net;
using System.Text;
using System.Text.Json;
using Basket.Infrastructure.ExternalServices.Catalog;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;

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
        // Arrange
        var productId = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { productId, sku = "SKU-A", name = "Widget", price = new { amount = 9.99m, currency = "USD" } })));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            productId,
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var productId = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            productId,
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<NotFoundError>(e => e.ErrorCode == "Basket.ProductNotFound")
                .Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductSnapshot_WhenHttp500_ReturnsCatalogUnavailable()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductSnapshot_WhenHttp400_ReturnsCatalogUnavailable()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductSnapshot_WhenHttpClientTimeout_ReturnsCatalogUnavailable()
    {
        // Arrange — HttpClient.Timeout firing surfaces as TaskCanceledException with an
        // inner TimeoutException; the caller token is NOT cancelled.
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new TaskCanceledException("simulated http timeout", new TimeoutException()));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    public async Task GetProductSnapshot_WhenCallerCancels_ThrowsOperationCanceled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHttpMessageHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var sut = CreateSut(handler);

        // Act
        var act = async () => await sut.GetProductSnapshotAsync(Guid.CreateVersion7(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductSnapshot_WhenHttpRequestException_ReturnsCatalogUnavailable()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("simulated DNS failure"));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductSnapshot_WhenMalformedJson_ReturnsCatalogUnavailable()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        }));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task GetMany_WhenEmptyInput_SkipsHttpCallAndReturnsEmptyList()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HttpClient must not be called with empty input."));

        // Act
        var result = await CreateSut(handler).GetManyAsync(
            Array.Empty<Guid>(),
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
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

        // Act
        var result = await CreateSut(handler).GetManyAsync(
            new[] { id1, id2, id3 },
            TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
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

        // Act
        var result = await CreateSut(handler).GetManyAsync(
            new[] { id1, id2, id3 },
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().HaveCount(2);
            result.Value.Select(p => p.ProductId).Should().BeEquivalentTo(new[] { id1, id2 });
            result.Value.Select(p => p.ProductId).Should().NotContain(id3);
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetMany_WhenHttp500_ReturnsCatalogUnavailable()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        // Act
        var result = await CreateSut(handler).GetManyAsync(
            new[] { Guid.CreateVersion7(), Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductSnapshot_WhenPriceIsNull_ReturnsCatalogUnavailable()
    {
        // Arrange — the single-product route owns its own record, so its strictness is not implied
        // by the batch route's: the two are independently declared and free to diverge, with no
        // compiler link between their annotations. This is the add-item path, where an unbound
        // price would reach Money.Create as a null dereference.
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { sku = "SKU-A", name = "Widget", price = (object?)null })));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetProductSnapshot_WhenPriceCurrencyMissing_ReturnsCatalogUnavailable()
    {
        // Arrange — a present price object missing one member. CatalogPriceDto is positional, so
        // only RespectRequiredConstructorParameters rejects this; a nulled price is caught by a
        // different setting entirely. Without it the currency binds null and Money.Create fails as
        // a *validation* error, reporting a Catalog contract break as a client fault.
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { sku = "SKU-A", name = "Widget", price = new { amount = 9.99m } })));

        // Act
        var result = await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetMany_WhenProductElementIsNull_ReturnsCatalogUnavailable()
    {
        // Arrange — System.Text.Json enforces nullability on members, not on collection *elements*,
        // so a null array item binds and no strict-binding setting rejects it. The adapter must
        // guard it explicitly or it dereferences into an uncaught NullReferenceException — a 500 on
        // the very path this ACL fails closed everywhere else.
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { products = new object?[] { null }, missingProductIds = Array.Empty<Guid>() })));

        // Act
        var result = await CreateSut(handler).GetManyAsync(
            new[] { Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task GetMany_WhenPriceIsNull_ReturnsCatalogUnavailable()
    {
        // Arrange — Catalog answers 200, but the item carries no bindable price. ADR-0037 leaves
        // the by-ids contract free to diverge from the single-product one, so this is a contract
        // change rather than a malformed body, and it must land in the same failure an unreachable
        // Catalog produces — never a snapshot composed from a half-bound product.
        var productId = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new
            {
                products = new[]
                {
                    new { productId, sku = "A", name = "Alpha", price = (object?)null },
                },
                missingProductIds = Array.Empty<Guid>(),
            })));

        // Act
        var result = await CreateSut(handler).GetManyAsync(
            new[] { productId },
            TestContext.Current.CancellationToken);

        // Assert
        AssertCatalogUnavailable(result);
    }

    [Fact]
    public async Task GetMany_JoinsIdsAsSingleCommaSeparatedQuery()
    {
        // Arrange
        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();
        var id3 = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { products = Array.Empty<object>(), missingProductIds = new[] { id1, id2, id3 } })));

        // Act
        _ = await CreateSut(handler).GetManyAsync(
            new[] { id1, id2, id3 },
            TestContext.Current.CancellationToken);

        // Assert
        handler.LastRequestPathAndQuery.Should().Be(
            $"/api/v1/catalog/products/by-ids?ids={id1:D},{id2:D},{id3:D}");
    }

    [Fact]
    [Trait("Category", "regression")]
    [Trait("Category", "boundary")]
    public async Task GetMany_WhenIdsExceedChunkSize_IssuesMultipleRequests()
    {
        // sum2.H-6 regression guard. The previous single-batch implementation built
        // ~38-byte-per-id query strings, so worst-case Basket.MaxItems=50 produced a
        // ~1900-char URL — uncomfortably close to common 2KB caps and brittle if the
        // basket-size limit ever rises. Chunking keeps each URL bounded.

        // Arrange
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.CreateVersion7()).ToArray();
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            // Each chunk reply contains an empty product list — the test only
            // cares about how many HTTP calls were made.
            var path = req.RequestUri!.PathAndQuery;
            path.Length.Should().BeLessThanOrEqualTo(1024,
                "each chunked URL must stay well under common 2KB caps");
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                new { products = Array.Empty<object>(), missingProductIds = Array.Empty<Guid>() }));
        });

        // Act
        var result = await CreateSut(handler).GetManyAsync(ids, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            handler.CallCount.Should().BeGreaterThan(1,
                "50 ids must be split across multiple chunks rather than a single oversized GET URL");
        }
    }

    [Fact]
    public async Task GetMany_DeduplicatesInputIds()
    {
        // Arrange
        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { products = Array.Empty<object>(), missingProductIds = new[] { id1, id2 } })));

        // Act
        _ = await CreateSut(handler).GetManyAsync(
            new[] { id1, id1, id2 },
            TestContext.Current.CancellationToken);

        // Assert
        handler.LastRequestPathAndQuery.Should().Be(
            $"/api/v1/catalog/products/by-ids?ids={id1:D},{id2:D}");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task GetProductSnapshot_WhenSkuIsBlank_ThrowsInsteadOfDegradingToCatalogUnavailable()
    {
        // A blank sku BINDS — strict binding rejects an absent or null member, not an empty string —
        // so this reaches the snapshot factory and throws. Every other failure mode on this adapter
        // returns CatalogUnavailable, and FetchChunkAsync maps its own snapshot-mapping failure that
        // way one line from the throw, which makes `catch (DataIntegrityException) => Unavailable`
        // read like the obvious missed case. It is not: a blank sku is Catalog emitting garbage, and
        // 503 would tell the caller to retry something retrying cannot fix. This pins that choice.
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new { sku = "", name = "Widget", price = new { amount = 9.99m, currency = "USD" } })));

        // Act
        var act = async () => await CreateSut(handler).GetProductSnapshotAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        (await act.Should().ThrowAsync<DataIntegrityException>())
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotSkuRequired");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task GetMany_WhenSkuIsBlank_ThrowsInsteadOfDegradingToCatalogUnavailable()
    {
        // The batch route binds its own record, so the single-product test does not imply this one.
        // This is the path where the adjacent mapResult.IsFailed branch already returns
        // CatalogUnavailable, so it is the likelier of the two to be "cleaned up" into a catch.
        var productId = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new
            {
                products = new[]
                {
                    new { productId, sku = "  ", name = "Widget", price = new { amount = 9.99m, currency = "USD" } },
                },
                missingProductIds = Array.Empty<Guid>(),
            })));

        // Act
        var act = async () => await CreateSut(handler).GetManyAsync(
            new[] { productId },
            TestContext.Current.CancellationToken);

        // Assert
        (await act.Should().ThrowAsync<DataIntegrityException>())
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotSkuRequired");
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task GetMany_WhenNameExceedsMaxLength_ThrowsInsteadOfDegradingToCatalogUnavailable()
    {
        // One route, not both: unlike the blank guards, the ceiling is reached through the shared
        // MapToSnapshot, so a per-route pair would kill one mutant twice. The batch route is the
        // one to keep — it reclassifies its own mapping failure as CatalogUnavailable a line later,
        // which is exactly where a "tidy up the length case" edit would land.
        // Name rather than Sku: Catalog's Name ceiling already equals Ordering's, zero headroom.
        var productId = Guid.CreateVersion7();
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            new
            {
                products = new[]
                {
                    new
                    {
                        productId,
                        sku = "SKU-A",
                        name = new string('x', 201),
                        price = new { amount = 9.99m, currency = "USD" },
                    },
                },
                missingProductIds = Array.Empty<Guid>(),
            })));

        // Act
        var act = async () => await CreateSut(handler).GetManyAsync(
            new[] { productId },
            TestContext.Current.CancellationToken);

        // Assert
        (await act.Should().ThrowAsync<DataIntegrityException>())
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotNameTooLong");
    }

    private static void AssertCatalogUnavailable<T>(Result<T> result)
    {
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<ServiceUnavailableError>(e => e.ErrorCode == "Basket.CatalogUnavailable")
                .Should().BeTrue();
        }
    }
}
