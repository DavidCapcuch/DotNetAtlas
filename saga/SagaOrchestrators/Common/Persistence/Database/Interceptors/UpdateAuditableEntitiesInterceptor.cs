using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.SharedKernel.Base;

namespace SagaOrchestrators.Persistence.Database.Interceptors;

/// <summary>
/// EF Core interceptor that automatically sets audit timestamps for saga entities
/// implementing <see cref="IAuditableEntity"/>.
/// </summary>
public sealed class UpdateAuditableEntitiesInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateAuditableEntitiesInterceptor"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for getting current UTC time.</param>
    public UpdateAuditableEntitiesInterceptor(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        var dbContext = eventData.Context;
        if (dbContext is null)
        {
            return base.SavingChanges(eventData, result);
        }

        var auditableEntries = dbContext.ChangeTracker.Entries<IAuditableEntity>();
        var utcNow = _timeProvider.GetUtcNow();

        foreach (var auditableEntry in auditableEntries)
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

        return base.SavingChanges(eventData, result);
    }
}
