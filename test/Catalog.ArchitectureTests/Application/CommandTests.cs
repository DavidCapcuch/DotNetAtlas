using NetArchTest.Rules;
using Platform.CQRS;

namespace Catalog.ArchitectureTests.Application;

/// <summary>
/// Per architecture-tests.md § 1.4, every command is named <c>*Command</c> and has a paired
/// <c>*CommandHandler</c>. Orphan commands signal dead code or a missing handler.
/// </summary>
public class CommandTests : BaseTest
{
    [Fact]
    public void Commands_Should_HaveNameEndingWith_Command()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface<ICommand>()
            .Or().ImplementInterface(typeof(ICommand<>))
            .Should()
            .HaveNameEndingWith("Command")
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Commands should follow the naming convention '*Command' for easy discovery and consistency");
    }

    [Fact]
    public void Commands_Should_HaveCorrespondingHandler()
    {
        var commandTypes = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface<ICommand>()
            .Or().ImplementInterface(typeof(ICommand<>))
            .GetTypes()
            .Select(i => i.ReflectionType)
            .ToHashSet();

        var handlerTypes = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or().ImplementInterface(typeof(ICommandHandler<,>))
            .GetTypes()
            .ToHashSet();

        var handledCommandTypes = handlerTypes
            .SelectMany(h => h.ReflectionType.GetInterfaces())
            .Where(i => i.IsGenericType &&
                        (i.GetGenericTypeDefinition() == typeof(ICommandHandler<>) ||
                         i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        var orphanCommands = commandTypes.Except(handledCommandTypes).ToList();

        orphanCommands.Should().BeEmpty(
            "Every command must have a corresponding handler. Orphan commands indicate dead code or " +
            "missing implementation. Found orphan commands: {0}",
            string.Join(", ", orphanCommands.Select(c => c.Name)));
    }
}
