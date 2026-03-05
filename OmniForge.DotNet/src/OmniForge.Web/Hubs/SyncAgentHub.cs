using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Hubs
{
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class SyncAgentHub : Hub
    {
        private readonly ISyncAgentTracker _tracker;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SyncAgentHub> _logger;

        public SyncAgentHub(ISyncAgentTracker tracker, IServiceScopeFactory scopeFactory, ILogger<SyncAgentHub> logger)
        {
            _tracker = tracker;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                await _tracker.RegisterAgentAsync(userId, Context.ConnectionId, "unknown");
                await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{userId}");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                await _tracker.UnregisterAgentAsync(userId, Context.ConnectionId);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"agent:{userId}");
            }
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Called by the sync agent to report the software type (obs/streamlabs).
        /// </summary>
        public async Task Identify(string softwareType)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return;

            await _tracker.RegisterAgentAsync(userId, Context.ConnectionId, softwareType);
            _logger.LogInformation("Sync agent identified: userId={UserId}, software={Software}", userId, softwareType);
        }

        /// <summary>
        /// Called by the sync agent to report all discovered scenes.
        /// Persists to ISceneRepository for configuration persistence.
        /// </summary>
        public async Task ReportScenes(string[] sceneNames, string source)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId) || sceneNames == null) return;

            using var scope = _scopeFactory.CreateScope();
            var sceneRepo = scope.ServiceProvider.GetRequiredService<ISceneRepository>();

            var now = DateTimeOffset.UtcNow;
            foreach (var name in sceneNames.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var existing = await sceneRepo.GetSceneAsync(userId, name);
                var scene = existing ?? new Scene
                {
                    UserId = userId,
                    Name = name,
                    Source = source,
                    FirstSeen = now
                };
                scene.LastSeen = now;
                scene.Source = source;
                await sceneRepo.SaveSceneAsync(scene);
            }

            _logger.LogInformation("Sync agent reported {Count} scenes for userId={UserId}", sceneNames.Length, userId);
        }

        /// <summary>
        /// Called by the sync agent when the active scene changes.
        /// </summary>
        public async Task ReportSceneChange(string sceneName)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return;

            var previousScene = _tracker.GetAgentState(userId)?.CurrentScene;
            await _tracker.UpdateCurrentSceneAsync(userId, sceneName);

            // Dispatch to scene action service
            using var scope = _scopeFactory.CreateScope();
            var sceneActionService = scope.ServiceProvider.GetService<ISceneActionService>();
            if (sceneActionService != null)
            {
                await sceneActionService.HandleSceneChangedAsync(userId, sceneName, previousScene);
            }

            _logger.LogInformation("Scene changed for userId={UserId}: {Previous} -> {New}", userId, previousScene ?? "(none)", sceneName);
        }

        /// <summary>
        /// Server-to-client: push checklist progress to the agent for tray tooltip.
        /// </summary>
        public static async Task SendChecklistProgress(IHubContext<SyncAgentHub> hubContext, string userId, int completed, int total)
        {
            await hubContext.Clients.Group($"agent:{userId}").SendAsync("ReceiveChecklistProgress", completed, total);
        }

        private string? GetUserId()
        {
            return Context.User?.FindFirst("userId")?.Value
                ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? Context.User?.FindFirst("sub")?.Value;
        }
    }
}
