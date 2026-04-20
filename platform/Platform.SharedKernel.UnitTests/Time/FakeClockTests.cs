using Platform.SharedKernel.Time;

namespace Platform.SharedKernel.UnitTests.Time;

public class FakeClockTests
{
    private static readonly DateTimeOffset Initial = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ctor_PinsUtcNowToInitialInstant()
    {
        // Arrange & Act
        var clock = new FakeClock(Initial);

        // Assert
        clock.UtcNow.Should().Be(Initial);
    }

    [Fact]
    public void Advance_WithPositiveDelta_MovesForward()
    {
        // Arrange
        var clock = new FakeClock(Initial);

        // Act
        clock.Advance(TimeSpan.FromMinutes(30));

        // Assert
        clock.UtcNow.Should().Be(Initial.AddMinutes(30));
    }

    [Fact]
    public void Advance_WithNegativeDelta_MovesBackward()
    {
        // Arrange
        var clock = new FakeClock(Initial);

        // Act
        clock.Advance(TimeSpan.FromHours(-2));

        // Assert
        clock.UtcNow.Should().Be(Initial.AddHours(-2));
    }

    [Fact]
    public void Set_OverridesCurrentInstant()
    {
        // Arrange
        var clock = new FakeClock(Initial);
        var target = Initial.AddYears(1);

        // Act
        clock.Set(target);

        // Assert
        clock.UtcNow.Should().Be(target);
    }

    [Fact]
    public void Advance_WhenCalledRepeatedly_Accumulates()
    {
        // Arrange
        var clock = new FakeClock(Initial);

        // Act
        clock.Advance(TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromSeconds(30));

        // Assert
        clock.UtcNow.Should().Be(Initial.AddMinutes(15).AddSeconds(30));
    }

    [Fact]
    public void ImplementsIClock()
    {
        // Assert
        typeof(FakeClock).Should().BeAssignableTo<IClock>();
    }
}
