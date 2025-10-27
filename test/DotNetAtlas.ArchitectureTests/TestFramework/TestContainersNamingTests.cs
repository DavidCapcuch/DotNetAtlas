using DotNetAtlas.Test.Framework.Common;
using NetArchTest.Rules;

namespace DotNetAtlas.ArchitectureTests.TestFramework;

public class TestContainersNamingTests : BaseTest
{
    /// <summary>
    /// In the CI pipeline, docker images used by the TestContainers are cached and cache keys are created
    /// using the hash of *Container.cs files in the TestFrameworkAssembly.
    /// </summary>
    [Fact]
    public void All_TestContainers_With_ImageName_Property_End_With_Container()
    {
        var result = Types.InAssembly(TestFrameworkAssembly)
            .That()
            .ImplementInterface<ITestContainer>()
            .Should()
            .HaveNameEndingWith("Container")
            .GetResult();

        result.FailingTypes.Should().BeEmpty();
    }
}
