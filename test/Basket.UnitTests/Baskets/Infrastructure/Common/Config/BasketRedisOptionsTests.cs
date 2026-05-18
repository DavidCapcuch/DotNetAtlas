using System.ComponentModel.DataAnnotations;
using Basket.Infrastructure.Common.Config;

namespace Basket.UnitTests.Baskets.Infrastructure.Common.Config;

/// <summary>
/// Pins the lock-retry-budget vs lock-TTL invariant from sum2.H-7. The retry
/// budget (<c>LockRetryDelayMs * LockMaxRetries</c>) must be at least as long as
/// <c>LockTimeoutSeconds * 1000</c>; otherwise a contender gives up while the
/// holder's lock TTL is still valid — surfacing spurious
/// <c>BasketConcurrencyError</c>s under load.
/// </summary>
public class BasketRedisOptionsTests
{
    [Fact]
    public void Validate_WhenDefaults_Passes()
    {
        // Defaults must satisfy the invariant out of the box — otherwise any service
        // booting on appsettings defaults alone would fail-fast at startup.
        var options = new BasketRedisOptions();

        var errors = options.Validate(new ValidationContext(options)).ToList();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenRetryBudgetEqualsLockTtl_Passes()
    {
        var options = new BasketRedisOptions
        {
            LockTimeoutSeconds = 5,
            LockRetryDelayMs = 100,
            LockMaxRetries = 50, // 50 * 100 = 5000 ms = 5s
        };

        var errors = options.Validate(new ValidationContext(options)).ToList();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenRetryBudgetShorterThanLockTtl_FailsWithDescriptiveError()
    {
        // The pre-fix defaults: 20 * 50 = 1000 ms, but lock TTL is 5000 ms. Contender
        // gives up after 1 s while the holder has 4 s of valid lock remaining.
        var options = new BasketRedisOptions
        {
            LockTimeoutSeconds = 5,
            LockRetryDelayMs = 50,
            LockMaxRetries = 20,
        };

        var errors = options.Validate(new ValidationContext(options)).ToList();

        using (new AssertionScope())
        {
            errors.Should().ContainSingle();
            errors[0].ErrorMessage.Should().Contain("LockRetryDelayMs");
            errors[0].ErrorMessage.Should().Contain("LockTimeoutSeconds");
        }
    }
}
