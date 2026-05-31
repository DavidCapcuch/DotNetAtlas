using AwesomeAssertions.Primitives;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Fluent assertions over the event-store <c>EventType</c> column — a simple CLR type name
/// (e.g. <c>"StockReservedDomainEvent"</c>) written as <c>@event.GetType().Name</c>. Replaces
/// the repeated <c>EventType.Should().Be(nameof(T))</c> pattern in Inventory event-store
/// tests; <c>typeof(T).Name</c> is the value compared against.
/// </summary>
internal static class StockEventAssertions
{
    /// <summary>
    /// Asserts the <c>EventType</c> string equals <c>typeof(T).Name</c> — the simple CLR name
    /// stored in the <c>stock_events</c> event-store row.
    /// </summary>
    /// <typeparam name="T">The domain event type whose simple name the value must equal.</typeparam>
    public static AndConstraint<StringAssertions> BeEventType<T>(
        this StringAssertions assertions,
        string because = "",
        params object[] becauseArgs)
        where T : class
        => assertions.Be(typeof(T).Name, because, becauseArgs);
}
