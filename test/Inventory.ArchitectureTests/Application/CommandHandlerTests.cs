using NetArchTest.Rules;
using Platform.CQRS;

namespace Inventory.ArchitectureTests.Application;

/// <summary>
/// Command handlers are named <c>*CommandHandler</c> and sealed (each handler encapsulates
/// one use case). Inventory has six command handlers as of M7: <c>InitializeStockItem</c>,
/// <c>ReceiveStock</c>, <c>AdjustStock</c>, <c>ReserveStock</c>, <c>ConfirmReservation</c>,
/// <c>ReleaseReservation</c>.
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
