using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.SharedKernel.Base;

namespace Weather.Infrastructure.Persistence.Database.Interceptors;

public sealed class UpdateAuditableEntitiesInterceptor
    : SaveChangesInterceptor
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

        var auditableEntries = dbContext.ChangeTracker.Entries<IAuditableEntity>();
        var utcNow = _timeProvider.GetUtcNow();
        foreach (var auditableEntry in auditableEntries)
        {
            if (auditableEntry.State == EntityState.Added)
            {
                // this doesn't rely on slow, standard C# reflection
                auditableEntry.Property(nameof(IAuditableEntity.CreatedUtc)).CurrentValue = utcNow;
                auditableEntry.Property(nameof(IAuditableEntity.LastModifiedUtc)).CurrentValue = utcNow;
            }
            else if (auditableEntry.State == EntityState.Modified)
            {
                auditableEntry.Property(nameof(IAuditableEntity.LastModifiedUtc)).CurrentValue = utcNow;
            }
        }

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }
}
