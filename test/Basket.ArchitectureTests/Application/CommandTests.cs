using NetArchTest.Rules;
using Platform.CQRS;

namespace Basket.ArchitectureTests.Application;

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

    /// <summary>
    /// Command + query types must be sealed records. Public-class commands with mutable setters
    /// allow consumers to mutate the input after the validation pipeline has run; sealed records
    /// with init-only setters lock it down.
    /// </summary>
    [Fact]
    public void Commands_And_Queries_Should_BeSealedRecords()
    {
        var offenders = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface<ICommand>()
            .Or().ImplementInterface(typeof(ICommand<>))
            .Or().ImplementInterface(typeof(IQuery<>))
            .GetTypes()
            .Where(t => !IsSealedRecord(t.ReflectionType))
            .Select(t => t.ReflectionType.FullName!)
            .ToList();

        offenders.Should().BeEmpty(
            "Commands and queries must be sealed records (init-only setters keep them immutable post-validation)");
    }

    private static bool IsSealedRecord(Type type)
    {
        if (!type.IsSealed)
        {
            return false;
        }

        return type.GetMethod(
            "<Clone>$",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic) is not null;
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
