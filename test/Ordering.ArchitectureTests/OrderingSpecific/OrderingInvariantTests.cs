using System.Reflection;
using NetArchTest.Rules;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.ArchitectureTests.OrderingSpecific;

public sealed class OrderingInvariantTests : BaseTest
{
    [Fact]
    public void Address_Should_BeSealedAndImmutableExternally()
    {
        var result = Types.InAssembly(typeof(Address).Assembly)
            .That().HaveName(nameof(Address))
            .Should().BeSealed().And().BeImmutableExternally()
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Address is a sealed immutable VO (ADR-0011 + architecture-tests.md § 2.3)");
    }

    [Fact]
    public void OrderItems_Property_Should_BeIReadOnlyCollection()
    {
        var orderType = DomainAssembly.GetType("Ordering.Domain.Orders.Order")
            ?? throw new InvalidOperationException("Order type missing");
        var itemsProperty = orderType.GetProperty("Items")
            ?? throw new InvalidOperationException("Order.Items property missing");
        itemsProperty.PropertyType.IsGenericType.Should().BeTrue();
        itemsProperty.PropertyType.GetGenericTypeDefinition().Should().Be(typeof(IReadOnlyCollection<>));
    }

    [Fact]
    public void OrderItems_BackingField_Should_BePrivateList()
    {
        var orderType = DomainAssembly.GetType("Ordering.Domain.Orders.Order")
            ?? throw new InvalidOperationException("Order type missing");
        var backing = orderType.GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Expected private field _items on Order");
        backing.IsPrivate.Should().BeTrue();
        backing.FieldType.IsGenericType.Should().BeTrue();
        backing.FieldType.GetGenericTypeDefinition().Should().Be(typeof(List<>));
    }
}
