using Basket.Application.Common.Persistence;
using Basket.Domain.Baskets.Errors;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;

namespace Basket.UnitTests.Baskets.Application.Common;

public class BasketConcurrencyRetryTests
{
    [Fact]
    [Trait("Category", "resilience")]
    public async Task ExecuteAsync_WhenFirstAttemptSucceeds_DoesNotRetry()
    {
        // Arrange
        var calls = 0;

        // Act
        var result = await BasketConcurrencyRetry.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(Result.Ok());
        }, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            calls.Should().Be(1);
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task ExecuteAsync_WhenFirstAttemptFailsWithConcurrency_RetriesOnce()
    {
        // Arrange
        var calls = 0;
        var userId = Guid.CreateVersion7();

        // Act
        var result = await BasketConcurrencyRetry.ExecuteAsync(_ =>
        {
            calls++;
            if (calls == 1)
            {
                return Task.FromResult(
                    Result.Fail(new BasketConcurrencyError(userId, expected: 3, actual: 4)));
            }

            return Task.FromResult(Result.Ok());
        }, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            calls.Should().Be(2);
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task ExecuteAsync_WhenBothAttemptsFailWithConcurrency_PropagatesTheSecondFailure()
    {
        // Arrange
        var calls = 0;
        var userId = Guid.CreateVersion7();

        // Act
        var result = await BasketConcurrencyRetry.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(
                Result.Fail(new BasketConcurrencyError(userId, expected: 3, actual: 4)));
        }, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<BasketConcurrencyError>().Should().BeTrue();
            calls.Should().Be(2);
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task ExecuteAsync_WhenFirstAttemptFailsWithNonConcurrencyError_DoesNotRetry()
    {
        // Arrange
        var calls = 0;

        // Act
        var result = await BasketConcurrencyRetry.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(Result.Fail(BasketErrors.InvalidQuantity()));
        }, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            calls.Should().Be(1);
        }
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task ExecuteAsync_Generic_RetriesOnceAndReturnsValue()
    {
        // Arrange
        var calls = 0;
        var userId = Guid.CreateVersion7();
        var expected = Guid.CreateVersion7();

        // Act
        var result = await BasketConcurrencyRetry.ExecuteAsync<Guid>(_ =>
        {
            calls++;
            if (calls == 1)
            {
                return Task.FromResult(
                    Result.Fail<Guid>(new BasketConcurrencyError(userId, 1, 2)));
            }

            return Task.FromResult(Result.Ok(expected));
        }, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().Be(expected);
            calls.Should().Be(2);
        }
    }
}
