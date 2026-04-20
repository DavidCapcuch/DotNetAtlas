using Platform.SharedKernel.Pii;

namespace Platform.SharedKernel.UnitTests.Pii;

public class PiiAttributeTests
{
    [Fact]
    public void PiiAttribute_OnClass_IsReadableByReflection()
    {
        // Act
        var attributes = typeof(PiiMarkedSample).GetCustomAttributes(typeof(PiiAttribute), inherit: false);

        // Assert
        attributes.Should().ContainSingle().Which.Should().BeOfType<PiiAttribute>();
    }

    [Fact]
    public void PiiAttribute_OnProperty_IsReadableByReflection()
    {
        // Act
        var property = typeof(NonPiiSample).GetProperty(nameof(NonPiiSample.SensitiveField))!;
        var attribute = property.GetCustomAttributes(typeof(PiiAttribute), inherit: false).SingleOrDefault();

        // Assert
        attribute.Should().NotBeNull().And.BeOfType<PiiAttribute>();
    }

    [Fact]
    public void PiiAttribute_AllowsClassStructPropertyAndField()
    {
        // Act
        var usage = typeof(PiiAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        // Assert — allowed targets must include the four required surfaces
        var required = AttributeTargets.Class
                       | AttributeTargets.Struct
                       | AttributeTargets.Property
                       | AttributeTargets.Field;
        (usage.ValidOn & required).Should().Be(required);
    }

    [Fact]
    public void PiiAttribute_IsSealed()
    {
        typeof(PiiAttribute).IsSealed.Should().BeTrue();
    }

    [Pii]
    private sealed class PiiMarkedSample
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class NonPiiSample
    {
        [Pii]
        public string SensitiveField { get; set; } = string.Empty;
    }
}
