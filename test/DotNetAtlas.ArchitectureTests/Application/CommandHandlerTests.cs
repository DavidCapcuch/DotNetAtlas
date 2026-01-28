using DotNetAtlas.CQS;
using NetArchTest.Rules;

namespace DotNetAtlas.ArchitectureTests.Application;

/// <summary>
/// Architecture tests for command handlers.
/// </summary>
public class CommandHandlerTests : BaseTest
{
    /// <summary>
    /// Convention for easy discovery.
    /// </summary>
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

    /// <summary>
    /// Handlers should be sealed to prevent inheritance.
    /// Each handler encapsulates a single use case.
    /// </summary>
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
