using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmniForge.Web.Services
{
    public class HubConnectionWrapper : IHubConnection
    {
        private readonly HubConnection _hubConnection;

        public HubConnectionWrapper(HubConnection hubConnection)
        {
            _hubConnection = hubConnection;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return _hubConnection.StartAsync(cancellationToken);
        }

        public Task InvokeAsync(string methodName, object? arg1, CancellationToken cancellationToken = default)
        {
            return _hubConnection.InvokeAsync(methodName, arg1, cancellationToken);
        }

        public IDisposable On<T1>(string methodName, Action<T1> handler)
        {
            return _hubConnection.On(methodName, handler);
        }

        public IDisposable On<T1>(string methodName, Func<T1, Task> handler)
        {
            return _hubConnection.On(methodName, handler);
        }

        public ValueTask DisposeAsync()
        {
            return _hubConnection.DisposeAsync();
        }
    }
}
