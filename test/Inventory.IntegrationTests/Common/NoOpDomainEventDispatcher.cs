using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Inert dispatcher used by tests that exercise the event-store write path in
/// isolation (e.g. the optimistic-concurrency-retry tests) — no handlers
/// should fire against the intercepted DbContext because the projection
/// handlers are wired to the DI-managed <see cref="Inventory.Infrastructure.Persistence.Database.InventoryDbContext"/>,
/// not the ad-hoc race-scenario context.
/// </summary>
internal sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public static readonly NoOpDomainEventDispatcher Instance = new();

    public Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : DomainEvent => Task.CompletedTask;
}
