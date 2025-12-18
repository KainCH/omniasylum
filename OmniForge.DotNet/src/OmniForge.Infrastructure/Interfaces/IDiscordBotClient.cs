using System.Threading.Tasks;
using Discord;

namespace OmniForge.Infrastructure.Interfaces
{
    public interface IDiscordBotClient
    {
        Task<bool> ValidateChannelAsync(string channelId, string botToken);
        Task SendMessageAsync(string channelId, string botToken, string? content, Embed embed, MessageComponent? components, AllowedMentions allowedMentions);
    }
}
