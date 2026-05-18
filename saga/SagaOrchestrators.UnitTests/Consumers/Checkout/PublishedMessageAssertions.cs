using MassTransit.Testing;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Async-friendly accessors over <see cref="IPublishedMessageList"/>. The synchronous
/// <c>Select&lt;T&gt;()</c> variant trips CA1849 - this helper routes through
/// <see cref="IPublishedMessageList.SelectAsync{T}"/> to keep tests both compliant and concise.
/// </summary>
internal static class PublishedMessageAssertions
{
    public static async Task<T> GetSinglePublishedMessageAsync<T>(
        this IPublishedMessageList list,
        CancellationToken cancellationToken)
        where T : class
    {
        var messages = new List<IPublishedMessage<T>>();
        await foreach (var item in list.SelectAsync<T>(cancellationToken))
        {
            messages.Add(item);
        }

        var captured = Assert.Single(messages);
        Assert.NotNull(captured.Context);
        Assert.NotNull(captured.Context.Message);
        return captured.Context.Message;
    }
}
