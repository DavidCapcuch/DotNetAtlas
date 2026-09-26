using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using Inventory.Application.StockItems.Common;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.IntegrationTests.Common;

namespace Inventory.IntegrationTests.ApiEndpoints.Reservations;

/// <summary>
/// Integration coverage for <c>GET /api/v1/inventory/reservations/{reservationId}</c> — the
/// reservation-audit lookup. <c>AdminReadPolicy</c> (use-cases.md § 4.4.3 / inventory.md § 9.2):
/// these rows correlate a reservation to an <c>OrderId</c> (internal ops/audit data, not
/// shopper-facing), so the read is gated on the <c>admin</c> role AND a read-capable scope —
/// tighter than the public stock-availability display reads. A plain <c>inventory.read</c>
/// caller is forbidden; only an admin token succeeds.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class GetReservationTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset SeedUtc = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    public GetReservationTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenAnonymous_Returns401()
    {
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.NonAuthClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenReadOnlyScope_WithoutAdminRole_Returns403()
    {
        // inventory.read alone does not reach reservation-audit data — the admin-role half
        // of AdminReadPolicy gates it (least privilege over the OrderId-bearing rows).
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.ReadOnlyClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenWriteScope_WithoutAdminRole_Returns403()
    {
        // Proves the gate is the role, not the scope: a write-scoped token without the admin
        // role is still forbidden (defense-in-depth, mirrors WritePolicy).
        var reservationId = Guid.CreateVersion7();

        var response = await Fixture.HttpClientRegistry.WriteScopeNoAdminClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenAdmin_AndReservationMissing_Returns404()
    {
        // Arrange
        var reservationId = Guid.CreateVersion7();

        // Act
        var response = await Fixture.HttpClientRegistry.CommandsClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problem!.Errors.Should().ContainSingle(e => e.Code == "Inventory.Reservation.NotFound");
        }
    }

    [Fact]
    public async Task WhenAdmin_AndReservationExists_Returns200()
    {
        // Arrange
        var productId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        await Seed.ActiveReservationAsync(
            productId, reservationId, orderId, quantity: 4, SeedUtc, TestContext.Current.CancellationToken);

        // Act
        var response = await Fixture.HttpClientRegistry.CommandsClient
            .GetAsync($"/api/v1/inventory/reservations/{reservationId}", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var audit = await response.Content.ReadFromJsonAsync<ReservationAuditResponse>(TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();
        using (new AssertionScope())
        {
            audit!.ReservationId.Should().Be(reservationId);
            audit.ProductId.Should().Be(productId);
            audit.OrderId.Should().Be(orderId);
            audit.Status.Should().Be(ReservationStatus.Active);

            // Quantity and the unset resolution are the fields an operator reads to tell a live hold
            // from a settled one.
            audit.Quantity.Should().Be(4);
            audit.ResolvedAtUtc.Should().BeNull();
            audit.ReleaseReason.Should().BeNull();
        }
    }
}
