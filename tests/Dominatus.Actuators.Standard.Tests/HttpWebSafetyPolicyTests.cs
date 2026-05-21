using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Runtime.Commands;
using Dominatus.OptFlow;
using System.Net;
using System.Net.Http;

namespace Dominatus.Actuators.Standard.Tests;

public sealed class HttpWebSafetyPolicyTests
{
    [Fact]
    public void WebSafetyPolicyOptions_RejectsInvalidAllowedHosts()
    {
        var ex = Assert.Throws<ArgumentException>(() => new HttpWebSafetyActuationPolicy(new WebSafetyPolicyOptions
        {
            AllowedHosts = ["https://evil.example/path"]
        }));

        Assert.Contains("cannot contain", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebSafetyPolicyOptions_RejectsInvalidRules()
    {
        var ex = Assert.Throws<ArgumentException>(() => new HttpWebSafetyActuationPolicy(new WebSafetyPolicyOptions
        {
            BlockRules =
            [
                new WebSafetyRule(".doubleclick.net", WebSafetyCategory.Ad),
                new WebSafetyRule(".DOUBLECLICK.NET", WebSafetyCategory.Tracker)
            ]
        }));

        Assert.Contains("Duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebSafetyPolicyOptions_NormalizesHosts()
    {
        var policy = new HttpWebSafetyActuationPolicy(new WebSafetyPolicyOptions
        {
            AllowedHosts = ["ADS.Example.Com"],
            BlockRules = [new WebSafetyRule(".example.com", WebSafetyCategory.Ad)]
        });

        var decision = policy.Evaluate(NewCtx(), new HttpGetTextCommand("api", "https://ads.example.com/banner"));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void HttpWebSafetyPolicy_AllowsNonHttpCommands()
    {
        var decision = HttpWebSafetyPolicies.Default().Evaluate(NewCtx(), new DelayCommand(0.1f));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void HttpWebSafetyPolicy_BlocksKnownAdHost()
    {
        var decision = HttpWebSafetyPolicies.Default().Evaluate(NewCtx(), new HttpGetTextCommand("ads", "ads"));
        Assert.False(decision.Allowed);
        Assert.Contains("Ad rule", decision.Reason);
    }

    [Fact]
    public void HttpWebSafetyPolicy_BlocksKnownTrackerHost()
    {
        var decision = HttpWebSafetyPolicies.Default().Evaluate(NewCtx(), new HttpGetTextCommand("api", "https://www.google-analytics.com/collect"));
        Assert.False(decision.Allowed);
        Assert.Contains("Tracker", decision.Reason);
    }

    [Fact]
    public void HttpWebSafetyPolicy_BlocksKnownMalwareRule()
    {
        var decision = HttpWebSafetyPolicies.Default().Evaluate(NewCtx(), new HttpGetTextCommand("api", "download/malware-test"));
        Assert.False(decision.Allowed);
        Assert.Contains("Malware", decision.Reason);
    }

    [Fact]
    public void HttpWebSafetyPolicy_WhitelistAllowsBlockedHost()
    {
        var policy = HttpWebSafetyPolicies.Default(["ad.doubleclick.net"]);
        var decision = policy.Evaluate(NewCtx(), new HttpGetTextCommand("ads", "ads"));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void HttpWebSafetyPolicy_BlockMessageDoesNotLeakQuerySecrets()
    {
        var decision = HttpWebSafetyPolicies.Default().Evaluate(NewCtx(), new HttpGetTextCommand("api", "https://ad.doubleclick.net/ads", new Dictionary<string, string> { ["token"] = "supersecret" }));
        Assert.False(decision.Allowed);
        Assert.DoesNotContain("supersecret", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpWebSafetyPolicy_BlocksSuspiciousDestinationAboveThreshold()
    {
        var policy = new HttpWebSafetyActuationPolicy(new WebSafetyPolicyOptions
        {
            SuspicionThreshold = 0.6f
        });

        var decision = policy.Evaluate(NewCtx(), new HttpGetTextCommand("analytics.example.com", "/pixel/collect"));
        Assert.False(decision.Allowed);
        Assert.Contains("scored", decision.Reason);
    }

    [Fact]
    public void HttpWebSafetyPolicy_AllowsSuspiciousDestinationBelowThreshold()
    {
        var policy = new HttpWebSafetyActuationPolicy(new WebSafetyPolicyOptions
        {
            SuspicionThreshold = 0.95f
        });

        var decision = policy.Evaluate(NewCtx(), new HttpGetTextCommand("analytics.example.com", "/collect"));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void HttpWebSafetyPolicy_CanDisableSuspicionBlocking()
    {
        var policy = new HttpWebSafetyActuationPolicy(new WebSafetyPolicyOptions
        {
            BlockSuspiciousByDefault = false,
            SuspicionThreshold = 0.1f
        });

        var decision = policy.Evaluate(NewCtx(), new HttpGetTextCommand("ads.analytics.example.com", "/pixel/collect?utm_x=1"));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void HttpActuator_WebSafetyPolicy_DeniesBeforeTransport()
    {
        var called = false;
        var host = NewHost(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        host.AddPolicy(HttpWebSafetyPolicies.Default());

        var dispatch = host.Dispatch(NewCtx(host), new HttpGetTextCommand("api", "https://ad.doubleclick.net/ads?token=secret"));
        Assert.False(dispatch.Ok);
        Assert.False(called);
    }

    [Fact]
    public void HttpActuator_WebSafetyPolicy_WhitelistAllowsTransport()
    {
        var called = false;
        var host = NewHost(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        host.AddPolicy(HttpWebSafetyPolicies.Default(["ad.doubleclick.net"]));

        var dispatch = host.Dispatch(NewCtx(host), new HttpGetTextCommand("ads", "ads"));
        Assert.True(dispatch.Ok);
        Assert.True(called);
    }

    private static ActuatorHost NewHost(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var host = new ActuatorHost();
        host.RegisterStandardHttpActuators(new HttpActuatorOptions
        {
            Endpoints =
            [
                new AllowedHttpEndpoint("api", new Uri("https://example.com/api/")),
                new AllowedHttpEndpoint("ads", new Uri("https://ad.doubleclick.net/"))
            ]
        }, new DelegateHttpMessageHandler((request, _) => Task.FromResult(send(request))));
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
