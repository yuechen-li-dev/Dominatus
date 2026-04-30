using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Dominatus.Actuators.Standard.Tests;

public sealed class HttpActuatorsTests
{
    [Fact]
    public void HttpOptions_RejectsNoEndpoints()
    {
        var ex = Assert.Throws<ArgumentException>(() => new HttpRequestResolver(new HttpActuatorOptions()));
        Assert.Contains("At least one HTTP endpoint", ex.Message);
    }

    [Fact]
    public void HttpOptions_RejectsDuplicateEndpointNames()
    {
        var ex = Assert.Throws<ArgumentException>(() => new HttpRequestResolver(new HttpActuatorOptions
        {
            Endpoints = [
                new AllowedHttpEndpoint("api", new Uri("https://example.com/base/")),
                new AllowedHttpEndpoint("API", new Uri("https://example.com/other/"))
            ]
        }));

        Assert.Contains("Duplicate HTTP endpoint name", ex.Message);
    }

    [Fact]
    public void HttpOptions_RejectsNonHttpScheme()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AllowedHttpEndpoint("api", new Uri("ftp://example.com/base/")));
        Assert.Contains("http or https", ex.Message);
    }

    [Fact]
    public void HttpOptions_RejectsUserInfoInBaseUri()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AllowedHttpEndpoint("api", new Uri("https://user:pass@example.com/base/")));
        Assert.Contains("user info", ex.Message);
    }

    [Fact]
    public void HttpOptions_RejectsNonPositiveTimeout()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => NewResolver(new HttpActuatorOptions { Timeout = TimeSpan.Zero }));
        Assert.Contains("Timeout", ex.Message);
    }

    [Fact]
    public void HttpOptions_RejectsNonPositiveSizeLimits()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => NewResolver(new HttpActuatorOptions { MaxResponseBytes = 0 }));
        Assert.Contains("MaxResponseBytes", ex.Message);
    }

    [Fact]
    public void HttpResolver_RejectsUnknownEndpoint()
    {
        var resolver = NewResolver();
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("missing", "status", null, null));
    }

    [Fact]
    public void HttpResolver_AllowsRelativePath()
    {
        var resolved = NewResolver().Resolve("api", "v1/status", null, null);
        Assert.Equal("https://example.com/api/v1/status", resolved.Uri.ToString());
    }

    [Fact]
    public void HttpResolver_AllowsEmptyPath()
    {
        var resolved = NewResolver().Resolve("api", "", null, null);
        Assert.Equal("https://example.com/api/", resolved.Uri.ToString());
    }

    [Fact]
    public void HttpResolver_RejectsAbsoluteUriPath()
    {
        Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve("api", "https://evil.example/x", null, null));
    }

    [Fact]
    public void HttpResolver_RejectsProtocolRelativePath()
    {
        Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve("api", "//evil.example/x", null, null));
    }

    [Fact]
    public void HttpResolver_RejectsFragment()
    {
        Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve("api", "v1/status#fragment", null, null));
    }

    [Fact]
    public void HttpResolver_RejectsBasePathEscape()
    {
        Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve("api", "../admin", null, null));
    }

    [Fact]
    public void HttpResolver_AppendsAndEncodesQuery()
    {
        var resolved = NewResolver().Resolve("api", "search", new Dictionary<string, string>
        {
            ["q"] = "hello world",
            ["tag"] = "a/b"
        }, null);

        var escapedQuery = resolved.Uri.GetComponents(UriComponents.Query, UriFormat.UriEscaped);
        Assert.Equal("q=hello%20world&tag=a%2Fb", escapedQuery);
    }

    [Fact]
    public void HttpResolver_RejectsSensitiveHeaders()
    {
        Assert.Throws<InvalidOperationException>(() => NewResolver().Resolve("api", "status", null, new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer x"
        }));
    }

    [Fact]
    public void HttpResolver_AllowsConfiguredSafeHeaders()
    {
        var resolved = NewResolver().Resolve("api", "status", null, new Dictionary<string, string>
        {
            ["Accept"] = "text/plain"
        });

        Assert.Single(resolved.Headers);
    }

    [Fact]
    public void HttpGetText_ReturnsStatusTextAndHeaders()
    {
        var handler = NewHttpHandler(_ => HttpResponse(HttpStatusCode.OK, "hello", headers: new Dictionary<string, string[]> { ["X-Test"] = ["1"] }));
        var result = handler.Handle(null!, NewCtx(), default, new HttpGetTextCommand("api", "status"));

        var payload = Assert.IsType<HttpTextResult>(result.Payload);
        Assert.True(result.Ok);
        Assert.Equal(200, payload.StatusCode);
        Assert.Equal("hello", payload.Text);
        Assert.Equal("1", payload.Headers["X-Test"][0]);
    }

    [Fact]
    public void HttpGetText_ReturnsNonSuccessStatusAsResult()
    {
        var handler = NewHttpHandler(_ => HttpResponse(HttpStatusCode.NotFound, "missing"));
        var result = handler.Handle(null!, NewCtx(), default, new HttpGetTextCommand("api", "missing"));

        var payload = Assert.IsType<HttpTextResult>(result.Payload);
        Assert.True(result.Ok);
        Assert.False(payload.IsSuccessStatusCode);
        Assert.Equal(404, payload.StatusCode);
    }

    [Fact]
    public void HttpGetText_RejectsResponseOverMaxBytes()
    {
        var options = BaseOptions with { MaxResponseBytes = 3 };
        var handler = NewHttpHandler(_ => HttpResponse(HttpStatusCode.OK, "hello"), options);

        var result = handler.Handle(null!, NewCtx(), default, new HttpGetTextCommand("api", "status"));
        Assert.False(result.Ok);
        Assert.Contains("MaxResponseBytes", result.Error);
    }

    [Fact]
    public void HttpGetText_TransportExceptionFails()
    {
        var handler = NewHttpHandler(_ => throw new HttpRequestException("boom"));
        var result = handler.Handle(null!, NewCtx(), default, new HttpGetTextCommand("api", "status"));
        Assert.False(result.Ok);
        Assert.Contains("boom", result.Error);
    }

    [Fact]
    public void HttpGetText_TimeoutFails()
    {
        var handler = NewHttpHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
            return HttpResponse(HttpStatusCode.OK, "late");
        }, BaseOptions with { Timeout = TimeSpan.FromMilliseconds(30) });

        var result = handler.Handle(null!, NewCtx(), default, new HttpGetTextCommand("api", "status"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void HttpGetText_CancellationFailsOrPropagatesConsistently()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = NewHttpHandler(async (_, ct) =>
        {
            await Task.Delay(5, ct);
            return HttpResponse(HttpStatusCode.OK, "nope");
        });

        var result = handler.Handle(null!, NewCtx(cancellationToken: cts.Token), default, new HttpGetTextCommand("api", "status"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void HttpPostJson_SendsApplicationJsonBody()
    {
        HttpRequestMessage? seen = null;
        var handler = NewHttpHandler(req =>
        {
            seen = req;
            return HttpResponse(HttpStatusCode.OK, "ok");
        });

        var result = handler.Handle(null!, NewCtx(), default, new HttpPostJsonCommand("api", "submit", "{\"a\":1}"));
        Assert.True(result.Ok);
        Assert.Equal("application/json", seen!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void HttpPostJson_RejectsMalformedJsonBeforeTransport()
    {
        var called = false;
        var handler = NewHttpHandler(_ =>
        {
            called = true;
            return HttpResponse(HttpStatusCode.OK, "ok");
        });

        var result = handler.Handle(null!, NewCtx(), default, new HttpPostJsonCommand("api", "submit", "{"));
        Assert.False(result.Ok);
        Assert.False(called);
    }

    [Fact]
    public void HttpPostJson_RejectsRequestOverMaxBytes()
    {
        var handler = NewHttpHandler(_ => HttpResponse(HttpStatusCode.OK, "ok"), BaseOptions with { MaxRequestBytes = 2 });
        var result = handler.Handle(null!, NewCtx(), default, new HttpPostJsonCommand("api", "submit", "{\"a\":1}"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void HttpPostJson_ReturnsResponseText()
    {
        var handler = NewHttpHandler(_ => HttpResponse(HttpStatusCode.Created, "created"));
        var result = handler.Handle(null!, NewCtx(), default, new HttpPostJsonCommand("api", "submit", "{\"a\":1}"));
        var payload = Assert.IsType<HttpTextResult>(result.Payload);
        Assert.Equal("created", payload.Text);
    }

    [Fact]
    public void HttpPostText_SendsConfiguredContentType()
    {
        HttpRequestMessage? seen = null;
        var handler = NewHttpHandler(req =>
        {
            seen = req;
            return HttpResponse(HttpStatusCode.OK, "ok");
        });

        var result = handler.Handle(null!, NewCtx(), default, new HttpPostTextCommand("api", "submit", "hello", "text/markdown"));
        Assert.True(result.Ok);
        Assert.Equal("text/markdown", seen!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void HttpPostText_RejectsEmptyContentType()
    {
        var handler = NewHttpHandler(_ => HttpResponse(HttpStatusCode.OK, "ok"));
        var result = handler.Handle(null!, NewCtx(), default, new HttpPostTextCommand("api", "submit", "hello", " "));
        Assert.False(result.Ok);
    }

    [Fact]
    public void HttpPostText_RejectsRequestOverMaxBytes()
    {
        var handler = NewHttpHandler(_ => HttpResponse(HttpStatusCode.OK, "ok"), BaseOptions with { MaxRequestBytes = 2 });
        var result = handler.Handle(null!, NewCtx(), default, new HttpPostTextCommand("api", "submit", "hello"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void HttpRedirect_DefaultDoesNotAutoFollow()
    {
        var handler = NewHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Content = new StringContent("redirect")
            };
            response.Headers.Location = new Uri("https://example.com/next");
            return response;
        });

        var result = handler.Handle(null!, NewCtx(), default, new HttpGetTextCommand("api", "status"));
        var payload = Assert.IsType<HttpTextResult>(result.Payload);
        Assert.Equal(302, payload.StatusCode);
    }

    [Fact]
    public void ActuatorHost_HttpGetText_CompletesWithHttpTextResult()
    {
        var host = NewHost(_ => HttpResponse(HttpStatusCode.OK, "ok"));
        var dispatch = host.Dispatch(NewCtx(host), new HttpGetTextCommand("api", "status"));
        Assert.True(dispatch.Completed && dispatch.Ok);
        Assert.IsType<HttpTextResult>(dispatch.Payload);
    }

    [Fact]
    public void ActuatorHost_HttpPostJson_CompletesWithHttpTextResult()
    {
        var host = NewHost(_ => HttpResponse(HttpStatusCode.OK, "ok"));
        var dispatch = host.Dispatch(NewCtx(host), new HttpPostJsonCommand("api", "submit", "{\"a\":1}"));
        Assert.True(dispatch.Completed && dispatch.Ok);
        Assert.IsType<HttpTextResult>(dispatch.Payload);
    }

    [Fact]
    public void ActuatorHost_HttpPolicyViolation_CompletesFailureAndDoesNotCallTransport()
    {
        var called = false;
        var host = NewHost(_ =>
        {
            called = true;
            return HttpResponse(HttpStatusCode.OK, "ok");
        });

        var dispatch = host.Dispatch(NewCtx(host), new HttpGetTextCommand("api", "https://evil.example"));
        Assert.False(dispatch.Ok);
        Assert.False(called);
    }

    private static HttpActuatorOptions BaseOptions => new()
    {
        Endpoints = [new AllowedHttpEndpoint("api", new Uri("https://example.com/api/"))]
    };

    private static HttpRequestResolver NewResolver(HttpActuatorOptions? options = null)
        => new(options ?? BaseOptions);

    private static HttpActuationHandler NewHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> send, HttpActuatorOptions? options = null)
        => new(options ?? BaseOptions, new DelegateHttpMessageHandler((request, _) => Task.FromResult(send(request))));

    private static HttpActuationHandler NewHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync, HttpActuatorOptions? options = null)
        => new(options ?? BaseOptions, new DelegateHttpMessageHandler(sendAsync));

    private static HttpResponseMessage HttpResponse(HttpStatusCode code, string text, Dictionary<string, string[]>? headers = null)
    {
        var response = new HttpResponseMessage(code)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        };

        if (headers is not null)
        {
            foreach (var header in headers)
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return response;
    }

    private static ActuatorHost NewHost(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var host = new ActuatorHost();
        host.RegisterStandardHttpActuators(BaseOptions, new DelegateHttpMessageHandler((request, _) => Task.FromResult(send(request))));
        return host;
    }

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
            while (true) yield return Ai.Wait(999f);
        }
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
            => _sendAsync = sendAsync;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _sendAsync(request, cancellationToken);
    }
}
