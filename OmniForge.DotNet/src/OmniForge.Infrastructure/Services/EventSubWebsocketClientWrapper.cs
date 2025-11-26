using System;
using System.Threading.Tasks;
using OmniForge.Infrastructure.Interfaces;
using TwitchLib.EventSub.Core;
using TwitchLib.EventSub.Core.EventArgs;
using TwitchLib.EventSub.Core.EventArgs.Stream;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace OmniForge.Infrastructure.Services
{
    public class EventSubWebsocketClientWrapper : IEventSubWebsocketClientWrapper
    {
        private readonly EventSubWebsocketClient _client;

        public EventSubWebsocketClientWrapper(EventSubWebsocketClient client)
        {
            _client = client;
        }

        public event AsyncEventHandler<WebsocketConnectedArgs>? WebsocketConnected
        {
            add => _client.WebsocketConnected += value;
            remove => _client.WebsocketConnected -= value;
        }

        public event AsyncEventHandler<WebsocketDisconnectedArgs>? WebsocketDisconnected
        {
            add => _client.WebsocketDisconnected += value;
            remove => _client.WebsocketDisconnected -= value;
        }

        public event AsyncEventHandler<WebsocketReconnectedArgs>? WebsocketReconnected
        {
            add => _client.WebsocketReconnected += value;
            remove => _client.WebsocketReconnected -= value;
        }

        public event AsyncEventHandler<ErrorOccuredArgs>? ErrorOccurred
        {
            add => _client.ErrorOccurred += value;
            remove => _client.ErrorOccurred -= value;
        }

        public event AsyncEventHandler<StreamOnlineArgs>? StreamOnline
        {
            add => _client.StreamOnline += value;
            remove => _client.StreamOnline -= value;
        }

        public event AsyncEventHandler<StreamOfflineArgs>? StreamOffline
        {
            add => _client.StreamOffline += value;
            remove => _client.StreamOffline -= value;
        }

        public string SessionId => _client.SessionId;

        public Task ConnectAsync(Uri? url = null)
        {
            return _client.ConnectAsync(url);
        }

        public Task DisconnectAsync()
        {
            return _client.DisconnectAsync();
        }
    }
}
