using FluentValidation;
using NetArchTest.Rules;

namespace Basket.ArchitectureTests.Application;

public class ValidatorTests : BaseTest
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

        result.FailingTypes.Should().BeEmpty(
            "Validators should follow the naming convention '*Validator' for easy discovery and consistency");
    }
}
