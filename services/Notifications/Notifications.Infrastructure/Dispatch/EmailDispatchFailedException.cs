using Platform.SharedKernel.Exceptions;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Thrown by <see cref="EmailChannelDispatcher"/> when the email gateway returns <c>Result.Fail</c>
/// (e.g. SMTP transport down) <i>after</i> the Failed ledger row + delivery event have already been
/// recorded. Classified as <see cref="RetryableException"/> — NOT <see cref="DataIntegrityException"/>:
/// a transport fault is recoverable, not a bug, so a later retry can flip the same
/// <c>(NotificationId, Channel)</c> ledger row to <c>Dispatched</c>. <see cref="NotificationDispatchJob"/>'s
/// <c>[AutomaticRetry(ExceptOn = CriticalException)]</c> gates the Hangfire retry on exactly this split:
/// this <see cref="RetryableException"/> retries (up to 3×), while a bug-class
/// <see cref="DataIntegrityException"/> is excluded and parks Failed on the first attempt. The structured
/// <see cref="System.Exception.Data"/> entry carries a typed <c>NotificationId</c> for operators. Mirrors
/// Inventory's <c>ReservationReleaseFailedException : RetryableException</c> pattern.
/// </summary>
public sealed class EmailDispatchFailedException : RetryableException
{
    public EmailDispatchFailedException(Guid notificationId, string errorSummary)
        : base($"Email send failed for NotificationId={notificationId}: {errorSummary}")
    {
        Data["NotificationId"] = notificationId;
    }

    public EmailDispatchFailedException()
    {
    }
}
