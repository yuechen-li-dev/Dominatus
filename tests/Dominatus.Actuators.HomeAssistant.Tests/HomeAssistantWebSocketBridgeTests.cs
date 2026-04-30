using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using System.Text.Json;

namespace Dominatus.Actuators.HomeAssistant.Tests;

public sealed class HomeAssistantWebSocketBridgeTests
{
    [Fact]
    public void WebSocketOptions_RejectsInvalidBaseUri()
    {
        var relative = new HomeAssistantWebSocketOptions { BaseUri = new Uri("/relative", UriKind.Relative), AccessToken = "token", AllowedEntities = ["light.office"] };
        Assert.Throws<ArgumentException>(() => HomeAssistantWebSocketValidation.Validate(relative));

        var badScheme = new HomeAssistantWebSocketOptions { BaseUri = new Uri("ftp://ha.local/"), AccessToken = "token", AllowedEntities = ["light.office"] };
        Assert.Throws<ArgumentException>(() => HomeAssistantWebSocketValidation.Validate(badScheme));

        var withUser = new HomeAssistantWebSocketOptions { BaseUri = new Uri("http://user:pass@ha.local:8123/"), AccessToken = "token", AllowedEntities = ["light.office"] };
        Assert.Throws<ArgumentException>(() => HomeAssistantWebSocketValidation.Validate(withUser));
    }

    [Fact] public void WebSocketOptions_RejectsEmptyToken() => Assert.Throws<ArgumentException>(() => HomeAssistantWebSocketValidation.Validate(BaseOptions with { AccessToken = " " }));
    [Fact] public void WebSocketOptions_RejectsNoAllowedEntities() => Assert.Throws<ArgumentException>(() => HomeAssistantWebSocketValidation.Validate(BaseOptions with { AllowedEntities = [] }));
    [Fact] public void WebSocketOptions_RejectsDuplicateAllowedEntities() => Assert.Throws<ArgumentException>(() => HomeAssistantWebSocketValidation.Validate(BaseOptions with { AllowedEntities = ["light.office", "LIGHT.OFFICE"] }));

    [Fact]
    public void WebSocketOptions_NormalizesHttpBaseUriToWsApiWebSocket()
        => Assert.Equal("ws://homeassistant.local:8123/api/websocket", HomeAssistantWebSocketValidation.Validate(BaseOptions).WebSocketUri.ToString());

    [Fact]
    public void WebSocketOptions_NormalizesHttpsBaseUriToWssApiWebSocket()
        => Assert.Equal("wss://homeassistant.local/api/websocket", HomeAssistantWebSocketValidation.Validate(BaseOptions with { BaseUri = new Uri("https://homeassistant.local/") }).WebSocketUri.ToString());

    [Fact]
    public void WebSocketOptions_AcceptsExistingApiBasePath()
        => Assert.Equal("ws://homeassistant.local:8123/api/websocket", HomeAssistantWebSocketValidation.Validate(BaseOptions with { BaseUri = new Uri("http://homeassistant.local:8123/api/") }).WebSocketUri.ToString());

    [Fact]
    public async Task WebSocketBridge_ConnectsAuthenticatesAndSubscribes()
    {
        var transport = new FakeTransport([
            "{\"type\":\"auth_required\"}",
            "{\"type\":\"auth_ok\"}",
            "{\"id\":1,\"type\":\"result\",\"success\":true}",
            null
        ]);

        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);
        await bridge.RunAsync(world, _ => true, CancellationToken.None);

        Assert.Equal("ws://homeassistant.local:8123/api/websocket", transport.ConnectedUri!.ToString());
        Assert.Equal(2, transport.SentTexts.Count);

        using var auth = JsonDocument.Parse(transport.SentTexts[0]);
        Assert.Equal("auth", auth.RootElement.GetProperty("type").GetString());
        Assert.Equal("token-123", auth.RootElement.GetProperty("access_token").GetString());

        using var subscribe = JsonDocument.Parse(transport.SentTexts[1]);
        Assert.Equal(1, subscribe.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("subscribe_events", subscribe.RootElement.GetProperty("type").GetString());
        Assert.Equal("state_changed", subscribe.RootElement.GetProperty("event_type").GetString());
    }

    [Fact]
    public async Task WebSocketBridge_FailsOnMissingAuthRequired()
    {
        var transport = new FakeTransport(["{\"type\":\"oops\"}"]);
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.RunAsync(BuildWorld(), _ => true, CancellationToken.None));
        Assert.Contains("auth_required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebSocketBridge_FailsOnAuthInvalid()
    {
        var transport = new FakeTransport([
            "{\"type\":\"auth_required\"}",
            "{\"type\":\"auth_invalid\",\"message\":\"nope\"}"
        ]);

        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.RunAsync(BuildWorld(), _ => true, CancellationToken.None));
    }

    [Fact]
    public async Task WebSocketBridge_FailsOnSubscribeFailure()
    {
        var transport = new FakeTransport([
            "{\"type\":\"auth_required\"}",
            "{\"type\":\"auth_ok\"}",
            "{\"id\":1,\"type\":\"result\",\"success\":false}"
        ]);

        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.RunAsync(BuildWorld(), _ => true, CancellationToken.None));
    }

    [Fact]
    public async Task WebSocketBridge_DoesNotLeakTokenInFailureMessages()
    {
        var token = "super-secret-token";
        var options = BaseOptions with { AccessToken = token };
        var transport = new FakeTransport([
            "{\"type\":\"auth_required\"}",
            "{\"type\":\"auth_invalid\",\"message\":\"bad token\"}"
        ]);

        var bridge = new HomeAssistantWebSocketEventBridge(options, transport);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.RunAsync(BuildWorld(), _ => true, CancellationToken.None));
        Assert.DoesNotContain(token, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebSocketBridge_PublishesAllowedStateChangedToRecipients()
    {
        var evt = StateChanged("light.office", "off", "on");
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, evt, null]);
        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);

        await bridge.RunAsync(world, _ => true, CancellationToken.None);

        var got = ConsumeStateChanged(world.Agents[0]);
        Assert.Equal("light.office", got.EntityId);
        Assert.Equal("on", got.NewState);
    }

    [Fact]
    public async Task WebSocketBridge_IgnoresUnallowlistedEntity()
    {
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, StateChanged("light.denied", "off", "on"), null]);
        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);

        await bridge.RunAsync(world, _ => true, CancellationToken.None);

        var cursor = new EventCursor();
        Assert.False(world.Agents[0].Events.TryConsume<HomeAssistantStateChanged>(ref cursor, null, out _));
    }

    [Fact]
    public async Task WebSocketBridge_BroadcastsUsingRecipientPredicate()
    {
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, StateChanged("light.office", "off", "on"), null]);
        var world = BuildWorld();
        world.SetPublic(world.Agents[0].Id, new AgentSnapshot(world.Agents[0].Id, Team: 0, default, true));
        world.SetPublic(world.Agents[1].Id, new AgentSnapshot(world.Agents[1].Id, Team: 1, default, true));

        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);
        await bridge.RunAsync(world, snap => snap.Team == 0, CancellationToken.None);

        var cursor0 = new EventCursor();
        Assert.True(world.Agents[0].Events.TryConsume<HomeAssistantStateChanged>(ref cursor0, null, out _));

        var cursor1 = new EventCursor();
        Assert.False(world.Agents[1].Events.TryConsume<HomeAssistantStateChanged>(ref cursor1, null, out _));
    }

    [Fact]
    public async Task WebSocketBridge_PreservesOldAndNewState()
    {
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, StateChanged("light.office", "off", "on"), null]);
        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);

        await bridge.RunAsync(world, _ => true, CancellationToken.None);

        var got = ConsumeStateChanged(world.Agents[0]);
        Assert.Equal("off", got.OldState);
        Assert.Equal("on", got.NewState);
    }

    [Fact]
    public async Task WebSocketBridge_PreservesRawJson()
    {
        var json = StateChanged("light.office", "off", "on");
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, json, null]);
        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);

        await bridge.RunAsync(world, _ => true, CancellationToken.None);
        Assert.Equal(json, ConsumeStateChanged(world.Agents[0]).Json);
    }

    [Fact]
    public async Task WebSocketBridge_IgnoresNonStateChangedEvents()
    {
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, "{\"type\":\"event\",\"event\":{\"event_type\":\"call_service\",\"data\":{}}}", null]);
        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);

        await bridge.RunAsync(world, _ => true, CancellationToken.None);
        var cursor = new EventCursor();
        Assert.False(world.Agents[0].Events.TryConsume<HomeAssistantStateChanged>(ref cursor, null, out _));
    }

    [Fact]
    public async Task WebSocketBridge_HandlesMissingOldOrNewStateAsNull()
    {
        var json = "{\"id\":1,\"type\":\"event\",\"event\":{\"event_type\":\"state_changed\",\"data\":{\"entity_id\":\"light.office\",\"new_state\":{\"state\":\"on\"}}}}";
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, json, null]);
        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);

        await bridge.RunAsync(world, _ => true, CancellationToken.None);

        var got = ConsumeStateChanged(world.Agents[0]);
        Assert.Null(got.OldState);
        Assert.Equal("on", got.NewState);
    }

    [Fact]
    public async Task WebSocketBridge_IgnoresMalformedStateChangedWithoutEntityId()
    {
        var json = "{\"id\":1,\"type\":\"event\",\"event\":{\"event_type\":\"state_changed\",\"data\":{\"new_state\":{\"state\":\"on\"}}}}";
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, json, null]);
        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);

        await bridge.RunAsync(world, _ => true, CancellationToken.None);
        var cursor = new EventCursor();
        Assert.False(world.Agents[0].Events.TryConsume<HomeAssistantStateChanged>(ref cursor, null, out _));
    }

    [Fact]
    public async Task WebSocketBridge_CancellationClosesTransportAndReturns()
    {
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk], blockOnReceive: true);
        var world = BuildWorld();
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);
        using var cts = new CancellationTokenSource();

        var run = bridge.RunAsync(world, _ => true, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await run;
        Assert.True(transport.CloseCalled);
    }

    [Fact]
    public async Task WebSocketBridge_RemoteCloseReturns()
    {
        var transport = new FakeTransport([AuthRequired, AuthOk, SubscribeOk, null]);
        var bridge = new HomeAssistantWebSocketEventBridge(BaseOptions, transport);
        await bridge.RunAsync(BuildWorld(), _ => true, CancellationToken.None);
    }

    private static HomeAssistantStateChanged ConsumeStateChanged(AiAgent agent)
    {
        var cursor = new EventCursor();
        Assert.True(agent.Events.TryConsume<HomeAssistantStateChanged>(ref cursor, null, out var evt));
        return evt;
    }

    private static AiWorld BuildWorld()
    {
        var world = new AiWorld();
        world.Add(NewIdleAgent());
        world.Add(NewIdleAgent());
        return world;
    }

    private static AiAgent NewIdleAgent()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = static _ => Idle() });
        return new AiAgent(new HfsmInstance(graph));

        static IEnumerator<AiStep> Idle()
        {
            while (true)
                yield return Ai.Wait(1f);
        }
    }

    private static string StateChanged(string entityId, string oldState, string newState)
        => $"{{\"id\":1,\"type\":\"event\",\"event\":{{\"event_type\":\"state_changed\",\"data\":{{\"entity_id\":\"{entityId}\",\"old_state\":{{\"state\":\"{oldState}\"}},\"new_state\":{{\"state\":\"{newState}\"}}}}}}}}";

    private static HomeAssistantWebSocketOptions BaseOptions => new()
    {
        BaseUri = new Uri("http://homeassistant.local:8123/"),
        AccessToken = "token-123",
        AllowedEntities = ["light.office"]
    };

    private const string AuthRequired = "{\"type\":\"auth_required\"}";
    private const string AuthOk = "{\"type\":\"auth_ok\"}";
    private const string SubscribeOk = "{\"id\":1,\"type\":\"result\",\"success\":true}";

    private sealed class FakeTransport : IHomeAssistantWebSocketTransport
    {
        private readonly Queue<string?> _messages;
        private readonly bool _blockOnReceive;

        public FakeTransport(IEnumerable<string?> messages, bool blockOnReceive = false)
        {
            _messages = new Queue<string?>(messages);
            _blockOnReceive = blockOnReceive;
        }

        public List<string> SentTexts { get; } = [];
        public Uri? ConnectedUri { get; private set; }
        public bool CloseCalled { get; private set; }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            ConnectedUri = uri;
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            SentTexts.Add(text);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(int maxBytes, CancellationToken cancellationToken)
        {
            if (_blockOnReceive && _messages.Count == 0)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            }

            if (_messages.Count == 0)
                return null;

            var next = _messages.Dequeue();
            return next;
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            CloseCalled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
