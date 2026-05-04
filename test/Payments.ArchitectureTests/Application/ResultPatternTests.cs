using NetArchTest.Rules;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;

namespace Payments.ArchitectureTests.Application;

/// <summary>
/// Per architecture-tests.md § 1.5, the result-pattern split is architecturally enforced:
/// <list type="bullet">
///   <item>Aggregates only ever throw <see cref="DataIntegrityException"/> (bug path).</item>
///   <item>Command/query handlers never raw-throw <see cref="ArgumentException"/> /
///     <see cref="InvalidOperationException"/> / <see cref="ArgumentNullException"/> — those would
///     surface as 500s instead of being expressible as a <c>Result.Fail</c>.</item>
///   <item>Handler <c>HandleAsync</c> methods return <c>Task&lt;Result&gt;</c> or
///     <c>Task&lt;Result&lt;T&gt;&gt;</c> — never a plain <c>Task</c> or a raw domain type.</item>
/// </list>
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
            "Aggregates may only throw DataIntegrityException (bug path). User-actionable failures " +
            "must be returned as Result.Fail with a PaymentsErrors factory.");
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
            "Handlers must not raw-throw ArgumentException / InvalidOperationException / ArgumentNullException. " +
            "Return Result.Fail with a PaymentsErrors factory instead — see error-taxonomy.md § 3.5.");
    }

    /// <summary>
    /// Per architecture-tests.md § 1.4, every <c>HandleAsync</c> on a command/query handler must
    /// return <c>Task&lt;Result&gt;</c> or <c>Task&lt;Result&lt;T&gt;&gt;</c>. Forbids regressions
    /// where a future contributor returns a raw domain type (which would hide error paths) or
    /// plain <c>Task</c>.
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
