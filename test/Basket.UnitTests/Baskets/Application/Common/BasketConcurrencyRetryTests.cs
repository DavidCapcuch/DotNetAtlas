using Basket.Application.Common.Persistence;
using Basket.Domain.Baskets.Errors;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;

namespace Basket.UnitTests.Baskets.Application.Common;

public class BasketConcurrencyRetryTests
{
    [Fact]
    public async Task ExecuteAsync_WhenFirstAttemptSucceeds_DoesNotRetry()
    {
        var calls = 0;

        var result = await BasketConcurrencyRetry.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(Result.Ok());
        }, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            calls.Should().Be(1);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenFirstAttemptFailsWithConcurrency_RetriesOnce()
    {
        var calls = 0;
        var userId = Guid.CreateVersion7();

        var result = await BasketConcurrencyRetry.ExecuteAsync(_ =>
        {
            calls++;
            if (calls == 1)
            {
                return Task.FromResult(
                    Result.Fail(new BasketConcurrencyError(userId, Expected: 3, Actual: 4)));
            }

            return Task.FromResult(Result.Ok());
        }, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            calls.Should().Be(2);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenBothAttemptsFailWithConcurrency_PropagatesTheSecondFailure()
    {
        var calls = 0;
        var userId = Guid.CreateVersion7();

        var result = await BasketConcurrencyRetry.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(
                Result.Fail(new BasketConcurrencyError(userId, Expected: 3, Actual: 4)));
        }, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.HasError<BasketConcurrencyError>().Should().BeTrue();
            calls.Should().Be(2);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenFirstAttemptFailsWithNonConcurrencyError_DoesNotRetry()
    {
        var calls = 0;

        var result = await BasketConcurrencyRetry.ExecuteAsync(_ =>
        {
            calls++;
            return Task.FromResult(Result.Fail(BasketErrors.InvalidQuantity()));
        }, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            calls.Should().Be(1);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Generic_RetriesOnceAndReturnsValue()
    {
        var calls = 0;
        var userId = Guid.CreateVersion7();
        var expected = Guid.CreateVersion7();

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

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().Be(expected);
            calls.Should().Be(2);
        }
    }
}
