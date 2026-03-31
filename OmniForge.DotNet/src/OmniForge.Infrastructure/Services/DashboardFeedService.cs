using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class DashboardFeedService : IDashboardFeedService
    {
        private sealed class Subscriber : IDisposable
        {
            private readonly DashboardFeedService _owner;
            private readonly string _userId;
            public Action<DashboardChatMessage>? OnChat { get; }
            public Action<DashboardEvent>? OnEvent { get; }
            public Action<bool>? OnLiveChange { get; }

            public Subscriber(DashboardFeedService owner, string userId,
                Action<DashboardChatMessage>? onChat, Action<DashboardEvent>? onEvent, Action<bool>? onLiveChange)
            {
                _owner = owner;
                _userId = userId;
                OnChat = onChat;
                OnEvent = onEvent;
                OnLiveChange = onLiveChange;
            }

            public void Dispose() => _owner.Unsubscribe(_userId, this);
        }

        private readonly ILogger<DashboardFeedService> _logger;
        private readonly ConcurrentDictionary<string, List<Subscriber>> _subscribers = new();
        private readonly ConcurrentDictionary<string, bool> _liveStatus = new();

        public DashboardFeedService(ILogger<DashboardFeedService> logger)
        {
            _logger = logger;
        }

        public void PushChatMessage(string userId, DashboardChatMessage msg)
        {
            if (!_subscribers.TryGetValue(userId, out var list)) return;

            List<Subscriber> snapshot;
            lock (list) { snapshot = new List<Subscriber>(list); }

            foreach (var sub in snapshot)
            {
                if (sub.OnChat == null) continue;
                try { sub.OnChat(msg); }
                catch (Exception ex) { _logger.LogError(ex, "❌ DashboardFeedService: error dispatching chat message for {UserId}", userId); }
            }
        }

        public void PushEvent(string userId, DashboardEvent evt)
        {
            if (!_subscribers.TryGetValue(userId, out var list)) return;

            List<Subscriber> snapshot;
            lock (list) { snapshot = new List<Subscriber>(list); }

            foreach (var sub in snapshot)
            {
                if (sub.OnEvent == null) continue;
                try { sub.OnEvent(evt); }
                catch (Exception ex) { _logger.LogError(ex, "❌ DashboardFeedService: error dispatching event for {UserId}", userId); }
            }
        }

        public void SetLiveStatus(string userId, bool isLive)
        {
            _liveStatus[userId] = isLive;

            if (!_subscribers.TryGetValue(userId, out var list)) return;

            List<Subscriber> snapshot;
            lock (list) { snapshot = new List<Subscriber>(list); }

            foreach (var sub in snapshot)
            {
                if (sub.OnLiveChange == null) continue;
                try { sub.OnLiveChange(isLive); }
                catch (Exception ex) { _logger.LogError(ex, "❌ DashboardFeedService: error dispatching live status for {UserId}", userId); }
            }
        }

        public bool GetLiveStatus(string userId) =>
            _liveStatus.TryGetValue(userId, out var live) && live;

        public IDisposable Subscribe(string userId, Action<DashboardChatMessage>? onChat, Action<DashboardEvent>? onEvent, Action<bool>? onLiveChange = null)
        {
            var sub = new Subscriber(this, userId, onChat, onEvent, onLiveChange);
            var list = _subscribers.GetOrAdd(userId, _ => new List<Subscriber>());
            lock (list) { list.Add(sub); }
            return sub;
        }

        private void Unsubscribe(string userId, Subscriber sub)
        {
            if (!_subscribers.TryGetValue(userId, out var list)) return;
            lock (list)
            {
                list.Remove(sub);
                if (list.Count == 0)
                    _subscribers.TryRemove(userId, out _);
            }
        }
    }
}
