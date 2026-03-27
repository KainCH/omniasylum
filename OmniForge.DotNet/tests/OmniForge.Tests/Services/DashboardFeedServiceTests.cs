using Microsoft.Extensions.Logging.Abstractions;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace OmniForge.Tests.Services;

public class DashboardFeedServiceTests
{
    private readonly DashboardFeedService _sut;

    public DashboardFeedServiceTests()
    {
        _sut = new DashboardFeedService(NullLogger<DashboardFeedService>.Instance);
    }

    private static DashboardChatMessage MakeChatMessage(string userId = "user-1") =>
        new DashboardChatMessage(
            UserId: userId,
            Username: "testuser",
            DisplayName: "TestUser",
            Message: "Hello!",
            IsMod: false,
            IsBroadcaster: false,
            IsSubscriber: false,
            ColorHex: null,
            Timestamp: DateTimeOffset.UtcNow);

    private static DashboardEvent MakeEvent(string eventType = "test_event") =>
        new DashboardEvent(
            EventType: eventType,
            Description: "Something happened",
            Timestamp: DateTimeOffset.UtcNow,
            Extra: null);

    // ──────────────────────────────────────────────────────────────────────────
    // Subscribe / Unsubscribe — basic behaviour
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Subscribe_ReturnsDisposable()
    {
        using var sub = _sut.Subscribe("user-1", onChat: null, onEvent: null);
        Assert.NotNull(sub);
    }

    [Fact]
    public void Unsubscribe_ViaDispose_DoesNotThrow()
    {
        var sub = _sut.Subscribe("user-1", onChat: null, onEvent: null);
        var exception = Record.Exception(() => sub.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Unsubscribe_CalledTwice_DoesNotThrow()
    {
        var sub = _sut.Subscribe("user-1", onChat: null, onEvent: null);
        sub.Dispose();
        var exception = Record.Exception(() => sub.Dispose());
        Assert.Null(exception);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PushChatMessage
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PushChatMessage_SubscriberReceivesMessage()
    {
        DashboardChatMessage? received = null;
        using var sub = _sut.Subscribe("user-1", onChat: msg => received = msg, onEvent: null);

        var outgoing = MakeChatMessage("user-1");
        _sut.PushChatMessage("user-1", outgoing);

        Assert.Same(outgoing, received);
    }

    [Fact]
    public void PushChatMessage_AfterUnsubscribe_SubscriberDoesNotReceiveMessage()
    {
        DashboardChatMessage? received = null;
        var sub = _sut.Subscribe("user-1", onChat: msg => received = msg, onEvent: null);
        sub.Dispose();

        _sut.PushChatMessage("user-1", MakeChatMessage("user-1"));

        Assert.Null(received);
    }

    [Fact]
    public void PushChatMessage_MultipleSubscribers_AllReceiveMessage()
    {
        var receivedByA = false;
        var receivedByB = false;

        using var subA = _sut.Subscribe("user-1", onChat: _ => receivedByA = true, onEvent: null);
        using var subB = _sut.Subscribe("user-1", onChat: _ => receivedByB = true, onEvent: null);

        _sut.PushChatMessage("user-1", MakeChatMessage("user-1"));

        Assert.True(receivedByA);
        Assert.True(receivedByB);
    }

    [Fact]
    public void PushChatMessage_OnlyDispatchesToCorrectUserId()
    {
        DashboardChatMessage? receivedForUser1 = null;
        DashboardChatMessage? receivedForUser2 = null;

        using var sub1 = _sut.Subscribe("user-1", onChat: msg => receivedForUser1 = msg, onEvent: null);
        using var sub2 = _sut.Subscribe("user-2", onChat: msg => receivedForUser2 = msg, onEvent: null);

        var msgForUser1 = MakeChatMessage("user-1");
        _sut.PushChatMessage("user-1", msgForUser1);

        Assert.Same(msgForUser1, receivedForUser1);
        Assert.Null(receivedForUser2);
    }

    [Fact]
    public void PushChatMessage_NoSubscribers_DoesNotThrow()
    {
        var exception = Record.Exception(() => _sut.PushChatMessage("user-1", MakeChatMessage("user-1")));
        Assert.Null(exception);
    }

    [Fact]
    public void PushChatMessage_SubscriberThrows_OtherSubscribersStillReceive()
    {
        var secondReceived = false;

        using var sub1 = _sut.Subscribe("user-1", onChat: _ => throw new InvalidOperationException("boom"), onEvent: null);
        using var sub2 = _sut.Subscribe("user-1", onChat: _ => secondReceived = true, onEvent: null);

        // Should not propagate the exception from sub1
        var exception = Record.Exception(() => _sut.PushChatMessage("user-1", MakeChatMessage("user-1")));
        Assert.Null(exception);
        Assert.True(secondReceived);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PushEvent
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PushEvent_SubscriberReceivesEvent()
    {
        DashboardEvent? received = null;
        using var sub = _sut.Subscribe("user-1", onChat: null, onEvent: evt => received = evt);

        var outgoing = MakeEvent();
        _sut.PushEvent("user-1", outgoing);

        Assert.Same(outgoing, received);
    }

    [Fact]
    public void PushEvent_AfterUnsubscribe_SubscriberDoesNotReceiveEvent()
    {
        DashboardEvent? received = null;
        var sub = _sut.Subscribe("user-1", onChat: null, onEvent: evt => received = evt);
        sub.Dispose();

        _sut.PushEvent("user-1", MakeEvent());

        Assert.Null(received);
    }

    [Fact]
    public void PushEvent_MultipleSubscribers_AllReceiveEvent()
    {
        var receivedByA = false;
        var receivedByB = false;

        using var subA = _sut.Subscribe("user-1", onChat: null, onEvent: _ => receivedByA = true);
        using var subB = _sut.Subscribe("user-1", onChat: null, onEvent: _ => receivedByB = true);

        _sut.PushEvent("user-1", MakeEvent());

        Assert.True(receivedByA);
        Assert.True(receivedByB);
    }

    [Fact]
    public void PushEvent_OnlyDispatchesToCorrectUserId()
    {
        DashboardEvent? receivedForUser1 = null;
        DashboardEvent? receivedForUser2 = null;

        using var sub1 = _sut.Subscribe("user-1", onChat: null, onEvent: evt => receivedForUser1 = evt);
        using var sub2 = _sut.Subscribe("user-2", onChat: null, onEvent: evt => receivedForUser2 = evt);

        var evt = MakeEvent();
        _sut.PushEvent("user-1", evt);

        Assert.Same(evt, receivedForUser1);
        Assert.Null(receivedForUser2);
    }

    [Fact]
    public void PushEvent_NoSubscribers_DoesNotThrow()
    {
        var exception = Record.Exception(() => _sut.PushEvent("user-1", MakeEvent()));
        Assert.Null(exception);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SetLiveStatus / GetLiveStatus
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetLiveStatus_ReturnsFalseByDefault()
    {
        Assert.False(_sut.GetLiveStatus("user-1"));
    }

    [Fact]
    public void SetLiveStatus_True_GetLiveStatusReturnsTrue()
    {
        _sut.SetLiveStatus("user-1", true);
        Assert.True(_sut.GetLiveStatus("user-1"));
    }

    [Fact]
    public void SetLiveStatus_False_GetLiveStatusReturnsFalse()
    {
        _sut.SetLiveStatus("user-1", true);
        _sut.SetLiveStatus("user-1", false);
        Assert.False(_sut.GetLiveStatus("user-1"));
    }

    [Fact]
    public void SetLiveStatus_DispatchesToSubscribers()
    {
        bool? received = null;
        using var sub = _sut.Subscribe("user-1", onChat: null, onEvent: null, onLiveChange: live => received = live);

        _sut.SetLiveStatus("user-1", true);

        Assert.True(received);
    }

    [Fact]
    public void SetLiveStatus_MultipleSubscribers_AllReceiveLiveChange()
    {
        bool? receivedByA = null;
        bool? receivedByB = null;

        using var subA = _sut.Subscribe("user-1", onChat: null, onEvent: null, onLiveChange: v => receivedByA = v);
        using var subB = _sut.Subscribe("user-1", onChat: null, onEvent: null, onLiveChange: v => receivedByB = v);

        _sut.SetLiveStatus("user-1", true);

        Assert.True(receivedByA);
        Assert.True(receivedByB);
    }

    [Fact]
    public void SetLiveStatus_OnlyDispatchesToCorrectUserId()
    {
        bool? receivedForUser1 = null;
        bool? receivedForUser2 = null;

        using var sub1 = _sut.Subscribe("user-1", onChat: null, onEvent: null, onLiveChange: v => receivedForUser1 = v);
        using var sub2 = _sut.Subscribe("user-2", onChat: null, onEvent: null, onLiveChange: v => receivedForUser2 = v);

        _sut.SetLiveStatus("user-1", true);

        Assert.True(receivedForUser1);
        Assert.Null(receivedForUser2);
    }

    [Fact]
    public void SetLiveStatus_AfterUnsubscribe_DoesNotDispatch()
    {
        bool? received = null;
        var sub = _sut.Subscribe("user-1", onChat: null, onEvent: null, onLiveChange: v => received = v);
        sub.Dispose();

        _sut.SetLiveStatus("user-1", true);

        Assert.Null(received);
    }

    [Fact]
    public void SetLiveStatus_NoSubscribers_DoesNotThrow()
    {
        var exception = Record.Exception(() => _sut.SetLiveStatus("user-1", true));
        Assert.Null(exception);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetLiveStatus — isolation across user IDs
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetLiveStatus_IndependentPerUserId()
    {
        _sut.SetLiveStatus("user-1", true);

        Assert.True(_sut.GetLiveStatus("user-1"));
        Assert.False(_sut.GetLiveStatus("user-2"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Subscriber with no onChat callback registered — PushChatMessage does not call it
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PushChatMessage_SubscriberWithNullOnChat_DoesNotThrow()
    {
        // onChat is null but onEvent is wired — pushing a chat message should be silent
        using var sub = _sut.Subscribe("user-1", onChat: null, onEvent: _ => { });
        var exception = Record.Exception(() => _sut.PushChatMessage("user-1", MakeChatMessage("user-1")));
        Assert.Null(exception);
    }

    [Fact]
    public void PushEvent_SubscriberWithNullOnEvent_DoesNotThrow()
    {
        using var sub = _sut.Subscribe("user-1", onChat: _ => { }, onEvent: null);
        var exception = Record.Exception(() => _sut.PushEvent("user-1", MakeEvent()));
        Assert.Null(exception);
    }
}
