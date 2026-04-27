using NetArchTest.Rules;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;

namespace Inventory.ArchitectureTests.Application;

/// <summary>
/// The result-pattern split is architecturally enforced for Inventory:
/// <list type="bullet">
///   <item>The <c>StockItem</c> aggregate only ever throws <see cref="DataIntegrityException"/>
///     (bug-class violations: unknown reservation, re-init, negative stock). Business-expected
///     failures — <c>InsufficientStock</c>, <c>ReservationNotActive</c> — flow through
///     <c>Result.Fail</c> with an <c>InventoryErrors</c> factory error per error-taxonomy.md § 3.4.</item>
///   <item>Command/query handlers never raw-throw <see cref="ArgumentException"/> /
///     <see cref="InvalidOperationException"/> / <see cref="ArgumentNullException"/> — those
///     would surface as 500s on saga-command Kafka consumers (which would then DLT the message)
///     instead of being expressible as a <c>Result.Fail</c> + outbox <c>StockReservationFailedEvent</c>.</item>
///   <item>Every handler <c>HandleAsync</c> returns <c>Task&lt;Result&gt;</c> /
///     <c>Task&lt;Result&lt;T&gt;&gt;</c>.</item>
/// </list>
/// Pinned by <c>inventory.md</c> M2 carry-forward "ReserveStockCommandHandler doesn't throw"
/// — InsufficientStock is BUSINESS-EXPECTED and must flow through Result.
/// </summary>
public class ResultPatternTests : BaseTest
{
    [Fact]
    public void Aggregates_Should_OnlyThrow_DataIntegrityException()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .Should()
            .MeetCustomRule(new OnlyThrowsRule(typeof(DataIntegrityException)))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "StockItem may only throw DataIntegrityException (bug path: unknown reservation, " +
            "re-init, negative stock). User-actionable failures (InsufficientStock, " +
            "ReservationNotActive) must be returned as Result.Fail with an InventoryErrors factory.");
    }

    [Fact]
    public void Handlers_ShouldNot_Throw_ArgumentOrInvalidOperationException()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("CommandHandler")
            .Or().HaveNameEndingWith("QueryHandler")
            .Should()
            .MeetCustomRule(new DoesNotThrowRule(
                typeof(ArgumentException),
                typeof(InvalidOperationException),
                typeof(ArgumentNullException)))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Inventory handlers must not raw-throw ArgumentException / InvalidOperationException / " +
            "ArgumentNullException. Return Result.Fail with an InventoryErrors factory instead — " +
            "InsufficientStock + ReservationNotActive are business-expected outcomes that the saga " +
            "translates into outbox StockReservationFailedEvent / ReservationReleasedEvent.");
    }

    /// <summary>
    /// Every <c>HandleAsync</c> on a command/query handler must return <c>Task&lt;Result&gt;</c>
    /// or <c>Task&lt;Result&lt;T&gt;&gt;</c>. Forbids regressions where a future contributor
    /// returns a raw domain type or plain <c>Task</c>, hiding error paths.
    /// </summary>
    [Fact]
    public void Handlers_Should_Return_ResultOrResultOfT()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("CommandHandler")
            .Or().HaveNameEndingWith("QueryHandler")
            .Should()
            .MeetCustomRule(new HandlerReturnsResultRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Handler HandleAsync methods must return Task<Result> or Task<Result<T>> — never a raw " +
            "domain type or plain Task. The Result wrapper is the only sanctioned error-pathway.");
    }
}
