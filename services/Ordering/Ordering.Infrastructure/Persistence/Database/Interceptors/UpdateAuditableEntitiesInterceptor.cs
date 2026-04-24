using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.SharedKernel.Base;

namespace Ordering.Infrastructure.Persistence.Database.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditableEntity.CreatedUtc"/> and
/// <see cref="IAuditableEntity.LastModifiedUtc"/> from <see cref="TimeProvider"/>
/// on save. Singleton-safe — no per-request state.
/// </summary>
internal sealed class UpdateAuditableEntitiesInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;

    public UpdateAuditableEntitiesInterceptor(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var utcNow = _timeProvider.GetUtcNow();
        foreach (var auditableEntry in dbContext.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (auditableEntry.State == EntityState.Added)
            {
                auditableEntry.Property(nameof(IAuditableEntity.CreatedUtc)).CurrentValue = utcNow;
                auditableEntry.Property(nameof(IAuditableEntity.LastModifiedUtc)).CurrentValue = utcNow;
            }
            else if (auditableEntry.State == EntityState.Modified)
            {
                auditableEntry.Property(nameof(IAuditableEntity.LastModifiedUtc)).CurrentValue = utcNow;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
