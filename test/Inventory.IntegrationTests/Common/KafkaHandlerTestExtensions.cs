using KafkaFlow;
using Microsoft.Extensions.DependencyInjection;
using Platform.Test.Framework.Kafka;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Enters a slice through its Kafka message: the typed handler, resolved from its own DI scope as a
/// consumer would get it, handles the real Avro contract under a fake message context.
/// </summary>
internal static class KafkaHandlerTestExtensions
{
    public static async Task DispatchAsync<THandler, TMessage>(this IntegrationTestFixture fixture, TMessage message)
        where THandler : IMessageHandler<TMessage>
    {
        using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        await handler.Handle(
            FakeKafkaMessageContext.Create(cancellationToken: TestContext.Current.CancellationToken),
            message);
    }
}
