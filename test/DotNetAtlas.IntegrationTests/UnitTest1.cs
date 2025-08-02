using FluentAssertions;

namespace DotNetAtlas.IntegrationTests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        true.Should().BeTrue();
    }
}