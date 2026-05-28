using FluentResults;
using Platform.Api.Extensions;

namespace Platform.Api.UnitTests;

public class ResultMatchExtensionsTests
{
    [Fact]
    public async Task MatchAsync_generic_invokes_onSuccess_for_success()
    {
        var result = Result.Ok(42);
        var observed = 0;

        await result.MatchAsync(
            onSuccess: v =>
            {
                observed = v;
                return Task.CompletedTask;
            },
            onFailure: _ => Task.FromException(new InvalidOperationException("onFailure must not run")));

        observed.Should().Be(42);
    }

    [Fact]
    public async Task MatchAsync_generic_invokes_onFailure_for_failure()
    {
        var result = Result.Fail<int>(new Error("boom"));
        var onFailureRan = false;

        await result.MatchAsync(
            onSuccess: _ => Task.FromException(new InvalidOperationException("onSuccess must not run")),
            onFailure: _ =>
            {
                onFailureRan = true;
                return Task.CompletedTask;
            });

        onFailureRan.Should().BeTrue();
    }

    [Fact]
    public async Task MatchAsync_non_generic_invokes_onSuccess_for_success()
    {
        var result = Result.Ok();
        var onSuccessRan = false;

        await result.MatchAsync(
            onSuccess: () =>
            {
                onSuccessRan = true;
                return Task.CompletedTask;
            },
            onFailure: _ => Task.FromException(new InvalidOperationException("onFailure must not run")));

        onSuccessRan.Should().BeTrue();
    }

    [Fact]
    public async Task MatchAsync_non_generic_invokes_onFailure_for_failure()
    {
        var result = Result.Fail(new Error("boom"));
        var onFailureRan = false;

        await result.MatchAsync(
            onSuccess: () => Task.FromException(new InvalidOperationException("onSuccess must not run")),
            onFailure: _ =>
            {
                onFailureRan = true;
                return Task.CompletedTask;
            });

        onFailureRan.Should().BeTrue();
    }
}
