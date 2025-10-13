using DotNetAtlas.Application.Common.CQS;
using FluentValidation;
using NetArchTest.Rules;

namespace DotNetAtlas.ArchitectureTests.Application;

public class ApplicationTests : BaseTest
{
    [Fact]
    public void Validator_Should_HaveNameEndingWith_Validator()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .Inherit(typeof(AbstractValidator<>))
            .Should()
            .HaveNameEndingWith("Validator")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void CommandOrQueryHandler_Should_HaveNameEndingWith_Handler()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or().ImplementInterface(typeof(ICommandHandler<>))
            .Or().ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .HaveNameEndingWith("Handler")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

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

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Queries_Should_HaveNameEndingWith_Query()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .Should()
            .HaveNameEndingWith("Query")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
