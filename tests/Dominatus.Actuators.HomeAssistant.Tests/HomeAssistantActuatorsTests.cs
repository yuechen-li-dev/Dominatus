using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Dominatus.Actuators.HomeAssistant.Tests;

public sealed class HomeAssistantActuatorsTests
{
    [Fact] public void HomeAssistantOptions_RejectsNullBaseUri() => Assert.Throws<ArgumentNullException>(() => new HomeAssistantRequestResolver(BaseOptions with { BaseUri = null! }));
    [Fact] public void HomeAssistantOptions_RejectsRelativeBaseUri() => Assert.Throws<ArgumentException>(() => new HomeAssistantRequestResolver(BaseOptions with { BaseUri = new Uri("/relative", UriKind.Relative) }));
    [Fact] public void HomeAssistantOptions_RejectsUserInfoBaseUri() => Assert.Throws<ArgumentException>(() => new HomeAssistantRequestResolver(BaseOptions with { BaseUri = new Uri("http://user:pass@ha.local:8123/") }));
    [Fact] public void HomeAssistantOptions_RejectsEmptyToken() => Assert.Throws<ArgumentException>(() => new HomeAssistantRequestResolver(BaseOptions with { AccessToken = "  " }));
    [Fact] public void HomeAssistantOptions_RejectsNoCapabilities() => Assert.Throws<ArgumentException>(() => new HomeAssistantRequestResolver(BaseOptions with { AllowedEntities = [], AllowedServices = [] }));

    [Fact]
    public void HomeAssistantOptions_RejectsDuplicateEntities()
        => Assert.Throws<ArgumentException>(() => new HomeAssistantRequestResolver(BaseOptions with { AllowedEntities = ["light.office_lamp", "LIGHT.OFFICE_LAMP"] }));

    [Fact]
    public void HomeAssistantOptions_RejectsDuplicateServices()
        => Assert.Throws<ArgumentException>(() => new HomeAssistantRequestResolver(BaseOptions with
        {
            AllowedServices = [
                new AllowedHomeAssistantService("light", "turn_on"),
                new AllowedHomeAssistantService("LIGHT", "TURN_ON")
            ]
        }));

    [Fact]
    public void HomeAssistantOptions_RejectsInvalidTimeoutOrSizeLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HomeAssistantRequestResolver(BaseOptions with { Timeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HomeAssistantRequestResolver(BaseOptions with { MaxResponseBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HomeAssistantRequestResolver(BaseOptions with { MaxRequestBytes = 0 }));
    }

    [Fact]
    public void GetState_RejectsUnallowedEntity()
        => Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve(new GetHomeAssistantStateCommand("light.not_allowed")));

    [Fact]
    public void CallService_RejectsUnallowedService()
        => Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve(new CallHomeAssistantServiceCommand("switch", "toggle", "{}")));

    [Fact]
    public void CallService_AllowsAllowedServiceWithoutEntityId()
    {
        var resolved = NewResolver().Resolve(new CallHomeAssistantServiceCommand("light", "turn_on", "{}"));
        Assert.Equal("http://homeassistant.local:8123/api/services/light/turn_on", resolved.Uri.ToString());
    }

    [Fact]
    public void CallService_AllowsAllowedServiceWithAllowedEntityId()
    {
        var resolved = NewResolver().Resolve(new CallHomeAssistantServiceCommand("light", "turn_on", "{\"entity_id\":\"light.office_lamp\"}"));
        Assert.Equal("light", resolved.Domain);
    }

    [Fact]
    public void CallService_RejectsServiceEntityIdNotGloballyAllowed()
        => Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve(new CallHomeAssistantServiceCommand("light", "turn_on", "{\"entity_id\":\"switch.desk_fan\"}")));

    [Fact]
    public void CallService_RejectsServiceEntityIdNotServiceAllowed()
        => Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve(new CallHomeAssistantServiceCommand("light", "turn_off", "{\"entity_id\":\"light.other\"}")));

    [Fact]
    public void CallService_ExtractsEntityIdStringArray()
    {
        var resolver = NewResolver(BaseOptions with
        {
            AllowedEntities = ["light.office_lamp", "switch.desk_fan"],
            AllowedServices = [new AllowedHomeAssistantService("light", "turn_on")]
        });

        var resolved = resolver.Resolve(new CallHomeAssistantServiceCommand("light", "turn_on", "{\"entity_id\":[\"light.office_lamp\",\"switch.desk_fan\"]}"));
        Assert.Equal("light", resolved.Domain);
    }

    [Fact]
    public void CallService_RejectsMalformedJson()
        => Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve(new CallHomeAssistantServiceCommand("light", "turn_on", "{")));

    [Fact]
    public void CallService_RejectsNonObjectJson()
        => Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve(new CallHomeAssistantServiceCommand("light", "turn_on", "[]")));

    [Fact]
    public void CallService_RejectsMalformedEntityIdProperty()
        => Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve(new CallHomeAssistantServiceCommand("light", "turn_on", "{\"entity_id\":123}")));

    [Fact]
    public void CallService_RejectsRequestOverMaxBytes()
        => Assert.Throws<InvalidOperationException>(() => NewResolver(BaseOptions with { MaxRequestBytes = 5 }).Resolve(new CallHomeAssistantServiceCommand("light", "turn_on", "{\"entity_id\":\"light.office_lamp\"}")));

    [Fact]
    public void GetState_SendsBearerTokenAndAcceptJson()
    {
        HttpRequestMessage? seen = null;
        using var handler = NewHandler(req =>
        {
            seen = req;
            return HttpResponse(HttpStatusCode.OK, "{\"entity_id\":\"light.office_lamp\",\"state\":\"on\"}");
        });

        var result = handler.Handle(null!, NewCtx(), default, new GetHomeAssistantStateCommand("light.office_lamp"));
        Assert.True(result.Ok);
        Assert.Equal("Bearer", seen!.Headers.Authorization!.Scheme);
        Assert.Equal("token-123", seen.Headers.Authorization.Parameter);
        Assert.Contains(seen.Headers.Accept, h => h.MediaType == "application/json");
    }

    [Fact]
    public void GetState_UsesApiStatesEndpoint()
    {
        HttpRequestMessage? seen = null;
        using var handler = NewHandler(req =>
        {
            seen = req;
            return HttpResponse(HttpStatusCode.OK, "{\"entity_id\":\"light.office_lamp\",\"state\":\"on\"}");
        }, BaseOptions with { BaseUri = new Uri("http://homeassistant.local:8123/") });

        _ = handler.Handle(null!, NewCtx(), default, new GetHomeAssistantStateCommand("light.office_lamp"));
        Assert.Equal("http://homeassistant.local:8123/api/states/light.office_lamp", seen!.RequestUri!.ToString());
    }

    [Fact]
    public void CallService_SendsBearerTokenAndJsonContent()
    {
        HttpRequestMessage? seen = null;
        using var handler = NewHandler(req =>
        {
            seen = req;
            return HttpResponse(HttpStatusCode.OK, "[]");
        });

        var result = handler.Handle(null!, NewCtx(), default, new CallHomeAssistantServiceCommand("light", "turn_on", "{\"entity_id\":\"light.office_lamp\"}"));
        Assert.True(result.Ok);
        Assert.Equal("Bearer", seen!.Headers.Authorization!.Scheme);
        Assert.Equal("application/json", seen.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void CallService_UsesApiServicesEndpoint()
    {
        HttpRequestMessage? seen = null;
        using var handler = NewHandler(req =>
        {
            seen = req;
            return HttpResponse(HttpStatusCode.OK, "[]");
        });

        _ = handler.Handle(null!, NewCtx(), default, new CallHomeAssistantServiceCommand("light", "turn_on", "{}"));
        Assert.Equal("http://homeassistant.local:8123/api/services/light/turn_on", seen!.RequestUri!.ToString());
    }

    [Fact]
    public void Handler_DoesNotLeakTokenInFailureMessages()
    {
        var handler = NewHandler(_ => throw new HttpRequestException("network error"));
        var result = handler.Handle(null!, NewCtx(), default, new GetHomeAssistantStateCommand("light.office_lamp"));
        Assert.False(result.Ok);
        Assert.DoesNotContain("token-123", result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void GetState_ReturnsEntityStateResult()
    {
        using var handler = NewHandler(_ => HttpResponse(HttpStatusCode.OK, "{\"entity_id\":\"light.office_lamp\",\"state\":\"on\"}"));
        var result = handler.Handle(null!, NewCtx(), default, new GetHomeAssistantStateCommand("light.office_lamp"));
        var payload = Assert.IsType<HomeAssistantEntityStateResult>(result.Payload);
        Assert.Equal("light.office_lamp", payload.EntityId);
        Assert.Equal("on", payload.State);
    }

    [Fact]
    public void GetState_NonSuccessFailsClearly()
    {
        using var handler = NewHandler(_ => HttpResponse(HttpStatusCode.NotFound, "missing"));
        var result = handler.Handle(null!, NewCtx(), default, new GetHomeAssistantStateCommand("light.office_lamp"));
        Assert.False(result.Ok);
        Assert.Contains("404", result.Error);
    }

    [Fact]
    public void GetState_ResponseOverMaxBytesFails()
    {
        using var handler = NewHandler(_ => HttpResponse(HttpStatusCode.OK, "{\"entity_id\":\"light.office_lamp\",\"state\":\"on\"}"), BaseOptions with { MaxResponseBytes = 4 });
        var result = handler.Handle(null!, NewCtx(), default, new GetHomeAssistantStateCommand("light.office_lamp"));
        Assert.False(result.Ok);
        Assert.Contains("MaxResponseBytes", result.Error);
    }

    [Fact]
    public void CallService_ReturnsStatusAndJson()
    {
        using var handler = NewHandler(_ => HttpResponse(HttpStatusCode.OK, "[{\"result\":\"ok\"}]"));
        var result = handler.Handle(null!, NewCtx(), default, new CallHomeAssistantServiceCommand("light", "turn_on", "{}"));
        var payload = Assert.IsType<HomeAssistantServiceCallResult>(result.Payload);
        Assert.Equal(200, payload.StatusCode);
        Assert.Equal("[{\"result\":\"ok\"}]", payload.Json);
    }

    [Fact]
    public void CallService_NonSuccessReturnsResult()
    {
        using var handler = NewHandler(_ => HttpResponse(HttpStatusCode.BadRequest, "{\"error\":\"bad\"}"));
        var result = handler.Handle(null!, NewCtx(), default, new CallHomeAssistantServiceCommand("light", "turn_on", "{}"));
        Assert.True(result.Ok);
        var payload = Assert.IsType<HomeAssistantServiceCallResult>(result.Payload);
        Assert.False(payload.IsSuccessStatusCode);
        Assert.Equal(400, payload.StatusCode);
    }

    [Fact]
    public void CallService_ResponseOverMaxBytesFails()
    {
        using var handler = NewHandler(_ => HttpResponse(HttpStatusCode.OK, "{}"), BaseOptions with { MaxResponseBytes = 1 });
        var result = handler.Handle(null!, NewCtx(), default, new CallHomeAssistantServiceCommand("light", "turn_on", "{}"));
        Assert.False(result.Ok);
        Assert.Contains("MaxResponseBytes", result.Error);
    }

    [Fact]
    public void TransportExceptionFailsClearly()
    {
        var handler = NewHandler(_ => throw new HttpRequestException("boom"));
        var result = handler.Handle(null!, NewCtx(), default, new CallHomeAssistantServiceCommand("light", "turn_on", "{}"));
        Assert.False(result.Ok);
        Assert.Contains("boom", result.Error);
    }

    [Fact]
    public void TimeoutFailsClearly()
    {
        using var handler = NewHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            return HttpResponse(HttpStatusCode.OK, "{}");
        }, BaseOptions with { Timeout = TimeSpan.FromMilliseconds(30) });

        var result = handler.Handle(null!, NewCtx(), default, new CallHomeAssistantServiceCommand("light", "turn_on", "{}"));
        Assert.False(result.Ok);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActuatorHost_GetState_CompletesWithEntityStateResult()
    {
        var host = NewHost(_ => HttpResponse(HttpStatusCode.OK, "{\"entity_id\":\"light.office_lamp\",\"state\":\"on\"}"));
        var dispatch = host.Dispatch(NewCtx(host), new GetHomeAssistantStateCommand("light.office_lamp"));
        Assert.True(dispatch.Completed && dispatch.Ok);
        Assert.IsType<HomeAssistantEntityStateResult>(dispatch.Payload);
    }

    [Fact]
    public void ActuatorHost_CallService_CompletesWithServiceCallResult()
    {
        var host = NewHost(_ => HttpResponse(HttpStatusCode.OK, "[]"));
        var dispatch = host.Dispatch(NewCtx(host), new CallHomeAssistantServiceCommand("light", "turn_on", "{}"));
        Assert.True(dispatch.Completed && dispatch.Ok);
        Assert.IsType<HomeAssistantServiceCallResult>(dispatch.Payload);
    }

    [Fact]
    public void ActuatorHost_PolicyViolation_DoesNotCallTransport()
    {
        var called = false;
        var host = NewHost(_ =>
        {
            called = true;
            return HttpResponse(HttpStatusCode.OK, "[]");
        });

        var dispatch = host.Dispatch(NewCtx(host), new CallHomeAssistantServiceCommand("light", "turn_on", "{\"entity_id\":\"light.denied\"}"));
        Assert.False(dispatch.Ok);
        Assert.False(called);
    }

    [Fact]
    public void DependencyGuard_NoForbiddenPackageReferences()
    {
        var projectPath = Path.Combine(ProjectRoot(), "src", "Dominatus.Actuators.HomeAssistant", "Dominatus.Actuators.HomeAssistant.csproj");
        var text = File.ReadAllText(projectPath);

        Assert.DoesNotContain("Dominatus.OptFlow", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dominatus.Llm.OptFlow", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ariadne.OptFlow", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stride", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MQTT", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HassClient", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NetDaemon", text, StringComparison.OrdinalIgnoreCase);
    }

    private static HomeAssistantActuatorOptions BaseOptions => new()
    {
        BaseUri = new Uri("http://homeassistant.local:8123/"),
        AccessToken = "token-123",
        AllowedEntities = ["light.office_lamp", "light.other"],
        AllowedServices =
        [
            new AllowedHomeAssistantService("light", "turn_on", ["light.office_lamp"]),
            new AllowedHomeAssistantService("light", "turn_off", ["light.office_lamp"]) 
        ]
    };

    private static HomeAssistantRequestResolver NewResolver(HomeAssistantActuatorOptions? options = null)
        => new(options ?? BaseOptions);

    private static HomeAssistantActuationHandler NewHandler(Func<HttpRequestMessage, HttpResponseMessage> send, HomeAssistantActuatorOptions? options = null)
        => new(options ?? BaseOptions, new DelegateHttpMessageHandler((request, _) => Task.FromResult(send(request))));

    private static HomeAssistantActuationHandler NewHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, HomeAssistantActuatorOptions? options = null)
        => new(options ?? BaseOptions, new DelegateHttpMessageHandler(send));

    private static ActuatorHost NewHost(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var host = new ActuatorHost();
        host.RegisterHomeAssistantActuators(BaseOptions, new DelegateHttpMessageHandler((request, _) => Task.FromResult(send(request))));
        return host;
    }

    private static HttpResponseMessage HttpResponse(HttpStatusCode statusCode, string text)
        => new(statusCode)
        {
            Content = new StringContent(text, Encoding.UTF8, "application/json")
        };

    private static AiCtx NewCtx(ActuatorHost? host = null, CancellationToken cancellationToken = default)
    {
        var world = new AiWorld(host ?? new ActuatorHost());
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = static _ => Idle() });

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, cancellationToken, world.View, world.Mail, world.Actuator);

        static IEnumerator<AiStep> Idle()
        {
            while (true)
                yield return Ai.Wait(999f);
        }
    }

    private static string ProjectRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
            => _sendAsync = sendAsync;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _sendAsync(request, cancellationToken);
    }
}
