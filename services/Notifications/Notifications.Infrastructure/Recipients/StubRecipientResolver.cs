using Notifications.Application.Recipients;

namespace Notifications.Infrastructure.Recipients;

/// <summary>
/// Walking-skeleton (#312) stub: derives a deterministic synthetic email from the recipient id so
/// the email channel has a valid address without a contact store. #314 replaces this with a lookup
/// over the seeded <c>user_preferences</c> table (real address, phone, timezone, quiet hours).
/// </summary>
internal sealed class StubRecipientResolver : IRecipientResolver
{
    // A reserved-for-documentation domain (RFC 6761) keeps the synthetic address obviously fake.
    private const string StubEmailDomain = "users.notifications.example";

    public Task<RecipientContact> ResolveAsync(Guid recipientUserId, CancellationToken ct)
    {
        var email = $"user-{recipientUserId:N}@{StubEmailDomain}";
        return Task.FromResult(new RecipientContact(email));
    }
}
