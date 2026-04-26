using NetArchTest.Rules;
using Platform.CQRS;

namespace Basket.ArchitectureTests.Application;

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
