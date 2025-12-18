using System.Collections.Generic;

namespace OmniForge.Core.Entities
{
    public class HelixCustomReward
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Cost { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string BackgroundColor { get; set; } = string.Empty;
        public bool IsUserInputRequired { get; set; }
        public HelixMaxPerStreamSetting MaxPerStreamSetting { get; set; } = new();
        public HelixMaxPerUserPerStreamSetting MaxPerUserPerStreamSetting { get; set; } = new();
        public HelixGlobalCooldownSetting GlobalCooldownSetting { get; set; } = new();
    }

    public class HelixMaxPerStreamSetting
    {
        public bool IsEnabled { get; set; }
        public int MaxPerStream { get; set; }
    }

    public class HelixMaxPerUserPerStreamSetting
    {
        public bool IsEnabled { get; set; }
        public int MaxPerUserPerStream { get; set; }
    }

    public class HelixGlobalCooldownSetting
    {
        public bool IsEnabled { get; set; }
        public int GlobalCooldownSeconds { get; set; }
    }

    /// <summary>
    /// Response from GET /chat/chatters endpoint.
    /// </summary>
    public class HelixChattersResponse
    {
        public List<HelixChatter> Data { get; set; } = new();
        public HelixPagination? Pagination { get; set; }
        public int Total { get; set; }
    }

    /// <summary>
    /// A chatter in the broadcaster's chat.
    /// </summary>
    public class HelixChatter
    {
        public string UserId { get; set; } = string.Empty;
        public string UserLogin { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from GET /users endpoint.
    /// </summary>
    public class HelixUsersResponse
    {
        public List<HelixUser> Data { get; set; } = new();
    }

    /// <summary>
    /// A Twitch user from the Helix API.
    /// </summary>
    public class HelixUser
    {
        public string Id { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ProfileImageUrl { get; set; } = string.Empty;
        public string BroadcasterType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pagination cursor for Helix API responses.
    /// </summary>
    public class HelixPagination
    {
        public string? Cursor { get; set; }
    }
}
