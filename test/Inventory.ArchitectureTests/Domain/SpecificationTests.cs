using Ardalis.Specification;
using Mono.Cecil;
using NetArchTest.Rules;

namespace Inventory.ArchitectureTests.Domain;

public class SpecificationTests : BaseTest
{
    /// <summary>
    /// Convention for easy discovery.
    /// </summary>
    [Fact]
    public void Specifications_Should_HaveNameEndingWith_Spec_Or_Specification()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(Specification<>))
            .Should()
            .MeetCustomRule(new SpecificationNamingRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Specifications should end with 'Spec' or 'Specification' for consistent naming");
    }

    private sealed class SpecificationNamingRule : ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            return type.Name.EndsWith("Spec", StringComparison.OrdinalIgnoreCase) ||
                   type.Name.EndsWith("Specification", StringComparison.OrdinalIgnoreCase);
        }
    }
}
