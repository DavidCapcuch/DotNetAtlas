using Platform.ServiceDefaults.Pii;
using Platform.SharedKernel.Pii;
using Serilog.Core;
using Serilog.Events;

namespace Platform.ServiceDefaults.UnitTests.Pii;

public class PiiDestructuringPolicyTests
{
    private static readonly ILogEventPropertyValueFactory Factory = new StubFactory();

    [Fact]
    public void TryDestructure_PiiMarkedType_ReturnsRedactedScalar()
    {
        // Arrange
        var policy = new PiiDestructuringPolicy();
        var value = new PiiMarkedAddress("221B Baker Street");

        // Act
        var handled = policy.TryDestructure(value, Factory, out var result);

        // Assert
        using var _ = new AssertionScope();
        handled.Should().BeTrue();
        result.Should().BeOfType<ScalarValue>();
        ((ScalarValue)result!).Value.Should().Be("***");
    }

    [Fact]
    public void TryDestructure_NonPiiType_ReturnsFalse()
    {
        // Arrange
        var policy = new PiiDestructuringPolicy();
        var value = new NonPiiOrderSummary("order-42", 99.99m);

        // Act
        var handled = policy.TryDestructure(value, Factory, out var result);

        // Assert
        using var _ = new AssertionScope();
        handled.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryDestructure_NullValue_ReturnsFalse()
    {
        // Arrange
        var policy = new PiiDestructuringPolicy();

        // Act
        var handled = policy.TryDestructure(null!, Factory, out var result);

        // Assert
        using var _ = new AssertionScope();
        handled.Should().BeFalse();
        result.Should().BeNull();
    }

    [Pii]
    private sealed record PiiMarkedAddress(string Street);

    private sealed record NonPiiOrderSummary(string OrderId, decimal Total);

    private sealed class StubFactory : ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false) =>
            new ScalarValue(value);
    }
}
