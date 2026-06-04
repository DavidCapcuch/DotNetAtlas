namespace Notifications.Application.Recipients;

/// <summary>
/// Resolves a recipient's contact details for channel delivery. The walking skeleton (#312) ships a
/// deterministic stub; #314 replaces it with a lookup over the seeded <c>user_preferences</c> table.
/// </summary>
public interface IRecipientResolver
{
    Task<RecipientContact> ResolveAsync(Guid recipientUserId, CancellationToken ct);
}
