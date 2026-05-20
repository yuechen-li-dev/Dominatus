using System.Text;
using Dominatus.Core;
using Dominatus.Core.Runtime;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;

namespace Dominatus.Actuators.SemanticKernel.Tests;

public sealed class SemanticKernelActuatorsTests
{
    private static readonly SemanticKernelActuatorOptions BaseOptions = new()
    {
        AllowedFunctions = [new("Tools", "Echo")], Timeout = TimeSpan.FromSeconds(1), MaxArgumentsBytes = 128, MaxResultBytes = 128
    };

    [Fact] public void SemanticKernelOptions_RejectsNoAllowedFunctions() => Assert.Throws<ArgumentException>(() => new SemanticKernelRequestResolver(BaseOptions with { AllowedFunctions = [] }));
    [Fact] public void SemanticKernelOptions_RejectsDuplicateFunctions() => Assert.Throws<ArgumentException>(() => new SemanticKernelRequestResolver(BaseOptions with { AllowedFunctions = [new("Tools","Echo"), new("tools","echo")] }));
    [Fact] public void SemanticKernelOptions_RejectsInvalidLimits() => Assert.Throws<ArgumentException>(() => new SemanticKernelRequestResolver(BaseOptions with { MaxArgumentsBytes = 0 }));
    [Fact] public void SemanticKernelOptions_RejectsInvalidTimeout() => Assert.Throws<ArgumentException>(() => new SemanticKernelRequestResolver(BaseOptions with { Timeout = TimeSpan.Zero }));

    [Fact] public void SemanticKernelCommand_RejectsUnallowedFunction() => Assert.Throws<InvalidOperationException>(() => new SemanticKernelRequestResolver(BaseOptions).Resolve(new("Tools", "Nope", "{}")));
    [Fact] public void SemanticKernelCommand_RejectsMalformedJson() => Assert.ThrowsAny<Exception>(() => new SemanticKernelRequestResolver(BaseOptions).Resolve(new("Tools", "Echo", "{")));
    [Fact] public void SemanticKernelCommand_RejectsNonObjectJson() => Assert.Throws<InvalidOperationException>(() => new SemanticKernelRequestResolver(BaseOptions).Resolve(new("Tools", "Echo", "[]")));
    [Fact] public void SemanticKernelCommand_RejectsArgumentsOverMaxBytes() => Assert.Throws<InvalidOperationException>(() => new SemanticKernelRequestResolver(BaseOptions with { MaxArgumentsBytes = 1 }).Resolve(new("Tools", "Echo", "{}")));

    [Fact]
    public void SemanticKernelCommand_MapsPrimitiveArguments()
    {
        var map = new SemanticKernelRequestResolver(BaseOptions).Resolve(new("Tools", "Echo", "{\"s\":\"x\",\"n\":2,\"d\":2.5,\"b\":true,\"z\":null}"));
        Assert.Equal("x", map["s"]); Assert.Equal(2L, map["n"]); Assert.Equal(2.5, map["d"]); Assert.Equal(true, map["b"]); Assert.Null(map["z"]);
    }

    [Fact] public void SemanticKernelCommand_RejectsNestedArgumentValues() => Assert.Throws<InvalidOperationException>(() => new SemanticKernelRequestResolver(BaseOptions).Resolve(new("Tools", "Echo", "{\"nested\":{}}")));

    [Fact]
    public void SemanticKernelHandler_InvokesAllowedFunction()
    {
        var invoker = new FakeInvoker((_, _, args, _) => Task.FromResult(args["a"]?.ToString() ?? string.Empty));
        var res = CreateHandler(invoker).Handle(new ActuatorHost(), MakeCtx(new ActuatorHost()), new(1), new("Tools", "Echo", "{\"a\":\"ok\"}"));
        Assert.True(res.Ok);
    }

    [Fact] public void SemanticKernelHandler_ReturnsTextResult() { var r = DispatchWithResult(Task.FromResult("hello")); Assert.Equal("hello", r.ResultText); }
    [Fact] public void SemanticKernelHandler_FailsWhenKernelFunctionMissing() { var h = CreateHandler(new FakeInvoker((_,_,_,_) => throw new InvalidOperationException("missing"))); var r = h.Handle(new(), MakeCtx(new()), new(1), new("Tools","Echo","{}")); Assert.False(r.Ok); }
    [Fact] public void SemanticKernelHandler_FailsWhenFunctionThrows() { var h = CreateHandler(new FakeInvoker((_,_,_,_) => throw new InvalidOperationException("boom"))); var r = h.Handle(new(), MakeCtx(new()), new(1), new("Tools","Echo","{}")); Assert.False(r.Ok); }
    [Fact] public void SemanticKernelHandler_FailsOnTimeoutOrCancellation() { var h = CreateHandler(new FakeInvoker(async (_,_,_,ct) => { await Task.Delay(2000, ct); return "x"; }), BaseOptions with { Timeout = TimeSpan.FromMilliseconds(10)}); var r = h.Handle(new(), MakeCtx(new()), new(1), new("Tools","Echo","{}")); Assert.False(r.Ok); }
    [Fact] public void SemanticKernelHandler_FailsWhenResultOverMaxBytes() { var h = CreateHandler(new FakeInvoker((_,_,_,_) => Task.FromResult(new string('a', 200))), BaseOptions with { MaxResultBytes = 10 }); var r = h.Handle(new(), MakeCtx(new()), new(1), new("Tools","Echo","{}")); Assert.False(r.Ok); }
    [Fact] public void SemanticKernelHandler_DoesNotLeakSensitiveDetailsInCommonFailures() { var h = CreateHandler(new FakeInvoker((_,_,_,_) => throw new InvalidOperationException("api key secret bad"))); var r = h.Handle(new(), MakeCtx(new()), new(1), new("Tools","Echo","{}")); Assert.Equal("Semantic Kernel function invocation failed.", r.Error); }

    [Fact]
    public void ActuatorHost_SemanticKernelFunction_CompletesWithResult()
    {
        var host = new ActuatorHost();
        host.Register<SemanticKernelFunctionCommand>(CreateHandler(new FakeInvoker((_,_,_,_) => Task.FromResult("ok"))));
        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("Tools","Echo","{}"));
        Assert.True(result.Ok);
    }

    [Fact]
    public void ActuatorHost_SemanticKernelPolicyViolation_DoesNotInvokeFunction()
    {
        var called = false;
        var host = new ActuatorHost();
        host.Register<SemanticKernelFunctionCommand>(CreateHandler(new FakeInvoker((_,_,_,_) => { called = true; return Task.FromResult("ok"); })));
        host.AddPolicy(ActuationPolicies.DenyAll("no"));
        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("Tools", "Echo", "{}"));
        Assert.False(result.Ok); Assert.False(called);
    }

    [Fact]
    public void DependencyGuard_NoForbiddenReferences()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var text = File.ReadAllText(Path.Combine(root,"src","Dominatus.Actuators.SemanticKernel","Dominatus.Actuators.SemanticKernel.csproj"));
        var forbidden = new[] { "Dominatus.OptFlow", "Dominatus.Llm.OptFlow", "Ariadne.OptFlow", "Stride", "HomeAssistant", "Mcp", "Planner", "Agents" };
        Assert.All(forbidden, f => Assert.DoesNotContain(f, text, StringComparison.OrdinalIgnoreCase));
    }

    private static SemanticKernelActuationHandler CreateHandler(ISemanticKernelFunctionInvoker invoker, SemanticKernelActuatorOptions? options = null) => new(invoker, options ?? BaseOptions);
    private static SemanticKernelFunctionResult DispatchWithResult(Task<string> task) => (SemanticKernelFunctionResult)CreateHandler(new FakeInvoker((_,_,_,_) => task)).Handle(new(), MakeCtx(new()), new(1), new("Tools","Echo","{}")).Payload!;

    private static AiCtx MakeCtx(ActuatorHost host)
    {
        var world = new AiWorld(host); var agent = new AiAgent(MakeBareBrain()); world.Add(agent);
        return new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host);
    }

    private static HfsmInstance MakeBareBrain()
    {
        var g = new HfsmGraph { Root = new StateId("root") };
        g.Add(new StateId("root"), static _ => Empty());
        return new HfsmInstance(g);
    }

    private static IEnumerator<AiStep> Empty() { yield break; }

    private sealed class FakeInvoker(Func<string, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<string>> impl) : ISemanticKernelFunctionInvoker
    {
        public Task<string> InvokeAsync(string pluginName, string functionName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
            => impl(pluginName, functionName, arguments, cancellationToken);
    }
}
