using Microsoft.EntityFrameworkCore;
using Notifications.Application.Common.Data;
using Notifications.Application.Recipients;
using Platform.SharedKernel.Exceptions;

namespace Notifications.Infrastructure.Recipients;

/// <summary>
/// Resolves a recipient's contact details from the seeded <c>user_preferences</c> table (notifications.md
/// § 8) — replaces the walking-skeleton synthetic-email stub (#312). A durable-channel dispatcher only runs
/// after the handler resolved that channel from the recipient's <i>enabled</i> set, which requires a
/// preference row; so a missing row at this point is a data-integrity violation (the row vanished after
/// resolution), not a normal "unprovisioned recipient" — hence the loud <see cref="DataIntegrityException"/>
/// rather than a silent skip.
/// </summary>
internal sealed class DbRecipientResolver : IRecipientResolver
{
    private readonly INotificationsDbContext _db;

    public DbRecipientResolver(INotificationsDbContext db)
    {
        _db = db;
    }

    public async Task<RecipientContact> ResolveAsync(Guid recipientUserId, CancellationToken ct)
    {
        var contact = await _db.UserPreferences
            .Where(p => p.UserId == recipientUserId)
            .Select(p => new RecipientContact(p.Email, p.PhoneNumber))
            .FirstOrDefaultAsync(ct);

        return contact ?? throw new DataIntegrityException(
            "Notifications.MissingRecipientPreference",
            $"No notification preference found for recipient {recipientUserId} when resolving contact details.");
    }
}
