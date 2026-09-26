using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.ReliableMessaging.Outbox.Core;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// While armed, fails any save that carries a newly added outbox row — the injected failure that
/// shows a delivery event and its ledger change commit or roll back together (ADR-0032 § 4).
/// </summary>
/// <remarks>
/// It catches an outbox row saved after the ledger committed, not one that commits no later than the
/// ledger — saved before the transaction, or on another connection inside it. Catching those takes a
/// commit-side trigger and a second failure case per channel. The fault is non-transient, so the
/// retrying execution strategy does not replay it.
/// </remarks>
internal sealed class OutboxSaveFaultInterceptor : SaveChangesInterceptor
{
    public const string FaultMessage = "Injected fault: a save carrying an outbox row.";

    private bool _armed;

    public void Arm() => _armed = true;

    public void Disarm() => _armed = false;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_armed && eventData.Context is { } context
            && context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added))
        {
            throw new InvalidOperationException(FaultMessage);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
