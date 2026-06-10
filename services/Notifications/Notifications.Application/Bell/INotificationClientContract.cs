using TypedSignalR.Client;

namespace Notifications.Application.Bell;

/// <summary>
/// Server-to-client contract for the in-app bell hub: the methods the server invokes on a
/// connected client. The bell only pushes (there is no client-to-server RPC — connections
/// auto-join their per-user group on connect), so there is no companion <c>[Hub]</c> contract.
/// </summary>
[Receiver]
public interface INotificationClientContract
{
    Task ReceiveNotification(BellNotification notification);
}
