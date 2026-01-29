using Microsoft.AspNetCore.SignalR.Client;

namespace OmniForge.Web.Services
{
    public interface IHubConnectionFactory
    {
        IHubConnection CreateConnection(Uri url);
    }

    public class HubConnectionFactory : IHubConnectionFactory
    {
        public IHubConnection CreateConnection(Uri url)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();
            return new HubConnectionWrapper(connection);
        }
    }
}
