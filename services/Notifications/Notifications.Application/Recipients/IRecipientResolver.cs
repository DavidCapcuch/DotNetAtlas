namespace Notifications.Application.Recipients;

/// <summary>
/// Resolves a recipient's contact details for channel delivery from the seeded <c>user_preferences</c>
/// table (#314, <c>DbRecipientResolver</c>) — replaced the #312 synthetic-email walking-skeleton stub.
/// </summary>
public interface IRecipientResolver
{
    Task<RecipientContact> ResolveAsync(Guid recipientUserId, CancellationToken ct);
}
