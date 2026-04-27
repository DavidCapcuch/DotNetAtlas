using FluentValidation;
using NetArchTest.Rules;

namespace Inventory.ArchitectureTests.Application;

/// <summary>
/// FluentValidation validators end in <c>*Validator</c> for predictable discovery + DI scanning
/// (<c>AddValidatorsFromAssembly</c> in <c>ApplicationDependencyInjection.AddApplication</c>).
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
