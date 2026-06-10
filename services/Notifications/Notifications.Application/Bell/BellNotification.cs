using MessagePack;

namespace Notifications.Application.Bell;

/// <summary>
/// Payload pushed to a recipient's in-app bell over SignalR, MessagePack-serialised to match the
/// hub's wire protocol. This thin placeholder is what the transport (#316) carries today; the
/// message <i>content</i> shape (title, link, severity, …) is finalised by the bell dispatcher
/// slice (#317). See ADR-0032 and notifications.md § 6.
/// </summary>
[MessagePackObject]
public sealed record BellNotification(
    [property: Key(0)] string Message);
