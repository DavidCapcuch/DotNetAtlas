using NetArchTest.Rules;
using Platform.CQRS;

namespace Invoicing.ArchitectureTests.Application;

public sealed class CommandHandlerTests : BaseTest
{
    [Fact]
    public void CommandHandlers_Should_HaveNameEndingWith_CommandHandler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That().ImplementInterface(typeof(ICommandHandler<>))
            .Or().ImplementInterface(typeof(ICommandHandler<,>))
            .Should().HaveNameEndingWith("CommandHandler")
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }

    [Fact]
    public void CommandHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That().ImplementInterface(typeof(ICommandHandler<>))
            .Or().ImplementInterface(typeof(ICommandHandler<,>))
            .Should().BeSealed()
            .GetResult();
        result.FailingTypes.Should().BeEmpty();
    }
}
