using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmniForge.Web.Services
{
    public interface IHubConnection : IAsyncDisposable
    {
        Task StartAsync(CancellationToken cancellationToken = default);
        Task InvokeAsync(string methodName, object? arg1, CancellationToken cancellationToken = default);
        IDisposable On<T1>(string methodName, Action<T1> handler);
        IDisposable On<T1>(string methodName, Func<T1, Task> handler);
    }
}
