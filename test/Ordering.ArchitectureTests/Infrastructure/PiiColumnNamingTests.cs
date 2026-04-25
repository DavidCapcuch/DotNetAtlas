using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence.Database;

namespace Ordering.ArchitectureTests.Infrastructure;

/// <summary>
/// ADR-0011 — every persisted column under the Address-typed owned entities
/// must be suffixed <c>_enc</c> so the v2 per-buyer DEK migration can flip
/// to ciphertext without renaming columns.
/// </summary>
public sealed class PiiColumnNamingTests
{
    [Theory]
    [InlineData(nameof(Order.ShippingAddress))]
    [InlineData(nameof(Order.BillingAddress))]
    public void Address_Columns_Should_AllEndWith_Enc(string navigationName)
    {
        using var context = CreateContextWithoutOpeningConnection();
        var orderEntity = context.Model.FindEntityType(typeof(Order))
            ?? throw new InvalidOperationException("Order entity not in model");
        var navigation = orderEntity.FindNavigation(navigationName)
            ?? throw new InvalidOperationException($"Navigation '{navigationName}' missing on Order");
        var ownedType = navigation.TargetEntityType;

        var nonEncColumns = ownedType.GetProperties()
            .Where(p => !p.IsShadowProperty())
            .Select(p => (Property: p.Name, Column: GetColumnName(p)))
            .Where(p => p.Column is not null && !p.Column!.EndsWith("_enc", StringComparison.Ordinal))
            .ToList();

        nonEncColumns.Should().BeEmpty(
            $"All Address columns under '{navigationName}' must be *_enc per ADR-0011 — " +
            $"violations: {string.Join(", ", nonEncColumns.Select(c => $"{c.Property}->{c.Column}"))}");
    }

    private static OrderingDbContext CreateContextWithoutOpeningConnection()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql("Host=fake;Database=fake;Username=fake;Password=fake")
            .Options;
        return new OrderingDbContext(options);
    }

    private static string? GetColumnName(IProperty property)
    {
        var storeObject = StoreObjectIdentifier.Create(property.DeclaringType, StoreObjectType.Table);
        return storeObject is { } so ? property.GetColumnName(so) : property.GetColumnName();
    }
}
