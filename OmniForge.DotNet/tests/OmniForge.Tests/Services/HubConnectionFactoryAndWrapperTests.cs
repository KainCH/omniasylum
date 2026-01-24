using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class HubConnectionFactoryAndWrapperTests
    {
        [Fact]
        public void HubConnectionFactory_ShouldCreateWrapper()
        {
            var factory = new HubConnectionFactory();
            var conn = factory.CreateConnection(new Uri("http://127.0.0.1:1/hub"));

            Assert.NotNull(conn);
            Assert.IsType<HubConnectionWrapper>(conn);
        }

        [Fact]
        public void HubConnectionWrapper_OnHandlers_ShouldReturnDisposable()
        {
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri("http://127.0.0.1:1/hub"))
                .Build();

            var wrapper = new HubConnectionWrapper(hub);

            using var d1 = wrapper.On<string>("method", _ => { });
            using var d2 = wrapper.On<string>("methodAsync", _ => Task.CompletedTask);

            Assert.NotNull(d1);
            Assert.NotNull(d2);
        }

        [Fact]
        public async Task HubConnectionWrapper_StartAsync_ShouldPropagateException_WhenNoServer()
        {
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri("http://127.0.0.1:1/hub"))
                .Build();

            var wrapper = new HubConnectionWrapper(hub);

            await Assert.ThrowsAnyAsync<Exception>(() => wrapper.StartAsync());
        }

        [Fact]
        public async Task HubConnectionWrapper_InvokeAsync_ShouldPropagateException_WhenNotStarted()
        {
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri("http://127.0.0.1:1/hub"))
                .Build();

            var wrapper = new HubConnectionWrapper(hub);

            await Assert.ThrowsAnyAsync<Exception>(() => wrapper.InvokeAsync("noop", new { X = 1 }));
        }

        [Fact]
        public async Task HubConnectionWrapper_DisposeAsync_ShouldNotThrow()
        {
            var hub = new HubConnectionBuilder()
                .WithUrl(new Uri("http://127.0.0.1:1/hub"))
                .Build();

            var wrapper = new HubConnectionWrapper(hub);

            await wrapper.DisposeAsync();
        }
    }
}
