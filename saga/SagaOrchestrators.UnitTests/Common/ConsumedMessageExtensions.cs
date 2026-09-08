using MassTransit.Testing;
using Platform.Test.Framework.Common;

namespace SagaOrchestrators.UnitTests.Common;

internal static class ConsumedMessageExtensions
{
    /// <summary>
    /// Waits until at least <paramref name="expectedCount"/> messages of type
    /// <typeparamref name="TMessage"/> have been consumed.
    /// </summary>
    /// <exception cref="TimeoutException">Thrown when that many messages never arrived.</exception>
    public static Task WaitForCountAsync<TMessage>(
        this IReceivedMessageList consumed,
        int expectedCount,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where TMessage : class =>
        Eventually.UntilAsync(
            async token =>
            {
                var seen = 0;
                await foreach (var _ in consumed.SelectAsync<TMessage>(token))
                {
                    if (++seen >= expectedCount)
                    {
                        return true;
                    }
                }

                return false;
            },
            timeout,
            $"{expectedCount} {typeof(TMessage).Name} message(s) to be consumed",
            cancellationToken);
}
