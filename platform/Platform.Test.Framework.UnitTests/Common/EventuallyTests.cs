using Platform.Test.Framework.Common;

namespace Platform.Test.Framework.UnitTests.Common;

/// <summary>
/// A suite waiting on <c>Eventually.UntilAsync</c> cannot itself tell "the condition never held"
/// from "the helper stopped asking", so a defect here surfaces as a flaky caller rather than as a
/// failure pointing back at this class.
/// </summary>
public sealed class EventuallyTests
{
    /// <summary>Shorter than the helper's poll interval, so the first delay is what trips it.</summary>
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task UntilAsync_WhenConditionHoldsOnFirstProbe_ReturnsWithoutPollingAgain()
    {
        // Arrange
        var probes = 0;

        // Act
        await Eventually.UntilAsync(
            _ =>
            {
                probes++;
                return Task.FromResult(true);
            },
            ShortTimeout,
            "the thing",
            TestContext.Current.CancellationToken);

        // Assert
        probes.Should().Be(1, "a condition that already holds must not cost a poll interval");
    }

    [Fact]
    public async Task UntilAsync_WhenConditionNeverHolds_ThrowsNamingWhatWasAwaited()
    {
        // Act
        var act = async () => await Eventually.UntilAsync(
            _ => Task.FromResult(false),
            ShortTimeout,
            "the basket tag to be evicted",
            TestContext.Current.CancellationToken);

        // Assert
        (await act.Should().ThrowExactlyAsync<EventuallyTimeoutException>())
            .WithMessage(
                "*the basket tag to be evicted*",
                "naming what was awaited is the whole reason this throws instead of returning false");
    }

    [Fact]
    public async Task UntilAsync_WhenCallersTokenIsAlreadyCancelled_PropagatesCancellationRatherThanTimingOut()
    {
        // Arrange
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var act = async () => await Eventually.UntilAsync(
            _ => Task.FromResult(false),
            ShortTimeout,
            "the thing",
            cancelled.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>(
            "a cancelled run is not a timeout; reporting it as one sends the reader hunting a slow dependency that was never slow");
    }

    [Fact]
    public async Task UntilAsync_WhenTheProbeAfterTheDeadlineStalls_FailsRatherThanHanging()
    {
        // Arrange — the caller's token stays live throughout, so only a deadline the helper imposes
        // on its own last probe can end this.
        var probes = 0;
        Func<CancellationToken, Task<bool>> stallsAfterFirstProbe = async token =>
        {
            if (probes++ == 0)
            {
                return false;
            }

            await Task.Delay(Timeout.Infinite, token);
            return true;
        };

        // Act
        var run = Eventually.UntilAsync(
            stallsAfterFirstProbe, ShortTimeout, "the thing", TestContext.Current.CancellationToken);
        // A watchdog, not an expected duration: the wait above should settle in about 200ms, so
        // anything reaching this bound means the stalled probe was never cut off at all.
        var settled = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        // Assert
        settled.Should().BeSameAs(
            run,
            "a helper that exists to bound a wait must bound its own last probe too, or a wedged dependency hangs the run with no stack trace");
        var act = async () => await run;
        await act.Should().ThrowExactlyAsync<EventuallyTimeoutException>(
            "a probe cut off by the helper's own budget means the condition was never observed to hold");
    }

    [Fact]
    public async Task UntilAsync_WhenTheProbeAfterTheDeadlineFaults_KeepsTheMessageAndCarriesTheFault()
    {
        // Arrange — the helper does I/O on its own failure path, at the moment a dependency is most
        // likely to be wedged, so a fault there is the likeliest way the message could be lost.
        var probes = 0;
        var fault = new InvalidOperationException("the datastore went away");
        Func<CancellationToken, Task<bool>> faultsAfterFirstProbe =
            _ => probes++ == 0 ? Task.FromResult(false) : throw fault;

        // Act
        var act = async () => await Eventually.UntilAsync(
            faultsAfterFirstProbe,
            ShortTimeout,
            "the basket tag to be evicted",
            TestContext.Current.CancellationToken);

        // Assert
        var thrown = await act.Should().ThrowExactlyAsync<EventuallyTimeoutException>(
            "a probe that faults on the way out must not displace the message naming what was awaited");
        thrown.WithMessage("*the basket tag to be evicted*");
        thrown.WithInnerException<InvalidOperationException>(
            "the fault is what a reader needs next, so it rides along rather than being swallowed");
    }

    [Fact]
    public async Task UntilAsync_WhenCallersTokenIsCancelledDuringTheProbeAfterTheDeadline_PropagatesCancellation()
    {
        // Arrange — cancelling while the post-deadline probe is in flight is the only window the
        // helper's second cancellation filter guards, and the outer one has already declined by then.
        using var caller = new CancellationTokenSource();
        var probes = 0;
        Func<CancellationToken, Task<bool>> cancelsDuringTheLastProbe = async token =>
        {
            if (probes++ == 0)
            {
                return false;
            }

            await caller.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return true;
        };

        // Act
        var act = async () => await Eventually.UntilAsync(
            cancelsDuringTheLastProbe,
            ShortTimeout,
            "the thing",
            caller.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>(
            "a run the caller cancelled is not a condition that never held; reported as one it reaches SagaStateMonitor as a saga that never transitioned");
    }

    [Fact]
    public async Task UntilAsync_WhenConditionHoldsOnlyAfterTheDeadline_StillSucceeds()
    {
        // Arrange — only a probe made after the deadline has expired can observe this condition.
        var probes = 0;

        // Act
        var act = async () => await Eventually.UntilAsync(
            _ => Task.FromResult(++probes > 1),
            ShortTimeout,
            "the thing",
            TestContext.Current.CancellationToken);

        // Assert
        await act.Should().NotThrowAsync(
            "the poll interval, not the condition, is what trips the deadline, so a run that succeeds as the clock crosses must not be failed");
    }
}
