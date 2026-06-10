using MessagePack;

namespace Notifications.Application.Bell;

/// <summary>
/// Payload pushed to a recipient's in-app bell over SignalR, MessagePack-serialised to match the
/// hub's wire protocol. <see cref="Message"/> is the fully-rendered <c>Bell</c> template body
/// (#317); a richer content shape (title, link, severity, …) is deferred with the bell's other
/// durability seams. See ADR-0032 and notifications.md § 6/§ 13.
/// </summary>
[MessagePackObject]
public sealed record BellNotification(
    [property: Key(0)] string Message);
