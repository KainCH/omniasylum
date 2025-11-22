using System;
using System.Threading.Tasks;
using TwitchLib.EventSub.Core;
using TwitchLib.EventSub.Core.EventArgs;
using TwitchLib.EventSub.Core.EventArgs.Stream;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace OmniForge.Infrastructure.Interfaces
{
    public interface IEventSubWebsocketClientWrapper
    {
        event AsyncEventHandler<WebsocketConnectedArgs> WebsocketConnected;
        event AsyncEventHandler<WebsocketDisconnectedArgs> WebsocketDisconnected;
        event AsyncEventHandler<WebsocketReconnectedArgs> WebsocketReconnected;
        event AsyncEventHandler<ErrorOccuredArgs> ErrorOccurred;
        event AsyncEventHandler<StreamOnlineArgs> StreamOnline;
        event AsyncEventHandler<StreamOfflineArgs> StreamOffline;

        string SessionId { get; }

        Task ConnectAsync(Uri? url = null);
        Task DisconnectAsync();
    }
}
