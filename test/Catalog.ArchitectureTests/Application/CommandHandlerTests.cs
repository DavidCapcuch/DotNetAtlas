using NetArchTest.Rules;
using Platform.CQRS;

namespace Catalog.ArchitectureTests.Application;

/// <summary>
/// Per architecture-tests.md § 1.4, command handlers are named <c>*CommandHandler</c> and sealed
/// (each handler encapsulates one use case).
/// </summary>
public class CommandHandlerTests : BaseTest
{
    [Fact]
    public void CommandHandlers_Should_HaveNameEndingWith_CommandHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or().ImplementInterface(typeof(ICommandHandler<,>))
            .Should()
            .HaveNameEndingWith("CommandHandler")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Command handlers should follow the naming convention '*CommandHandler' for easy discovery and consistency");
    }

    [Fact]
    public void CommandHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or().ImplementInterface(typeof(ICommandHandler<,>))
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Command handlers should be sealed - each encapsulates a single use case and inheritance would " +
            "break the single responsibility principle");
    }
}
