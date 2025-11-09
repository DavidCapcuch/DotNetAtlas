using DotNetAtlas.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DotNetAtlas.Infrastructure.Persistence.Database.Interceptors;

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
            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        var auditableEntries = dbContext.ChangeTracker.Entries<IAuditableEntity>();
        var utcNow = _timeProvider.GetUtcNow();
        foreach (var auditableEntry in auditableEntries)
        {
            if (auditableEntry.State == EntityState.Added)
            {
                auditableEntry.Entity.CreatedUtc = utcNow;
            }

            auditableEntry.Entity.LastModifiedUtc = utcNow;
        }

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }
}
