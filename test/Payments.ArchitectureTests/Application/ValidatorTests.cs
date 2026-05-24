using FluentValidation;
using NetArchTest.Rules;

namespace Payments.ArchitectureTests.Application;

/// <summary>
/// FluentValidation validators end in <c>*Validator</c> for predictable discovery and DI scanning.
/// </summary>
public class ValidatorTests : BaseTest
{
    [Fact]
    public void Validators_Should_HaveNameEndingWith_Validator()
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
