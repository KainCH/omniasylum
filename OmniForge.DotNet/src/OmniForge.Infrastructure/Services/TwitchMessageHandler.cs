using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Models;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class TwitchMessageHandler : ITwitchMessageHandler
    {
        private readonly IChatCommandProcessor _chatCommandProcessor;
        private readonly ILogger<TwitchMessageHandler> _logger;

        public TwitchMessageHandler(
            IChatCommandProcessor chatCommandProcessor,
            ILogger<TwitchMessageHandler> logger)
        {
            _chatCommandProcessor = chatCommandProcessor;
            _logger = logger;
        }

        public async Task HandleMessageAsync(string userId, ChatMessage chatMessage, Func<string, string, Task> sendMessage)
        {
            var context = new ChatCommandContext
            {
                UserId = userId,
                Message = chatMessage.Message,
                IsModerator = chatMessage.IsModerator,
                IsBroadcaster = chatMessage.IsBroadcaster,
                IsSubscriber = chatMessage.IsSubscriber
            };

            await _chatCommandProcessor.ProcessAsync(context, sendMessage);
        }
    }
}
