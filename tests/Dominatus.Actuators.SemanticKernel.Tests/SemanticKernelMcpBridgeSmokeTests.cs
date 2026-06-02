using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.SemanticKernel.Tests;

public sealed class SemanticKernelMcpBridgeSmokeTests
{
    [Fact]
    public void SemanticKernelMcpSmoke_AllowlistedMcpBackedFunction_InvokesThroughSkActuator()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("contents from mcp-backed function"));
        var handler = CreateHandler(invoker, [new("mcp.filesystem", "read_file")]);
        var host = new ActuatorHost();
        host.Register<SemanticKernelFunctionCommand>(handler);

        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("mcp.filesystem", "read_file", "{\"path\":\"README.md\"}"));

        Assert.True(result.Ok);
        var payload = Assert.IsType<SemanticKernelFunctionResult>(result.Payload);
        Assert.Equal("mcp.filesystem", payload.PluginName);
        Assert.Equal("read_file", payload.FunctionName);
        Assert.Equal("contents from mcp-backed function", payload.ResultText);

        var call = Assert.Single(invoker.Calls);
        Assert.Equal("mcp.filesystem", call.PluginName);
        Assert.Equal("read_file", call.FunctionName);
        Assert.Equal("README.md", call.Arguments["path"]);
    }

    [Fact]
    public void SemanticKernelMcpSmoke_UnallowlistedMcpBackedFunction_IsDeniedBeforeInvocation()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("should not run"));
        var handler = CreateHandler(invoker, [new("mcp.filesystem", "read_file")]);
        var host = new ActuatorHost();
        host.Register<SemanticKernelFunctionCommand>(handler);

        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("mcp.github", "list_issues", "{}"));

        Assert.False(result.Ok);
        Assert.Equal("Semantic Kernel function is not allowlisted.", result.Error);
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public void SemanticKernelMcpSmoke_MetadataCatalog_DoesNotAutoAllowDiscoveredMcpTools()
    {
        var options = new SemanticKernelActuatorOptions
        {
            AllowedFunctions = [new("mcp.filesystem", "read_file")]
        };

        var catalog = new SemanticKernelFunctionCatalog(
            new FakeMetadataReader(new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>
            {
                [("mcp.filesystem", "read_file")] = new("Read file", [new("path", "File path", "string", true)]),
                [("mcp.filesystem", "delete_file")] = new("Delete file", [new("path", "File path", "string", true)])
            }),
            options);

        var result = catalog.GetAllowedFunctions();

        var allowed = Assert.Single(result);
        Assert.Equal("mcp.filesystem", allowed.PluginName);
        Assert.Equal("read_file", allowed.FunctionName);
        Assert.True(allowed.IsAllowed);
        Assert.True(allowed.ExistsInKernel);
        Assert.DoesNotContain(result, x => x.FunctionName.Equals("delete_file", StringComparison.Ordinal));
    }

    [Fact]
    public void Dominatus_Actuators_SemanticKernel_DoesNotReferenceMcpPackages()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var text = File.ReadAllText(Path.Combine(root, "src", "Dominatus.Actuators.SemanticKernel", "Dominatus.Actuators.SemanticKernel.csproj"));
        var forbidden = new[] { "ModelContextProtocol", "Mcp", "MCP", "Anthropic.Mcp", "Microsoft.Mcp", "Microsoft.Graph", "Graph", "OpenApi", "A2A" };
        Assert.All(forbidden, f => Assert.DoesNotContain(f, text, StringComparison.OrdinalIgnoreCase));
    }

    private static SemanticKernelActuationHandler CreateHandler(RecordingInvoker invoker, IReadOnlyList<AllowedSemanticKernelFunction> allowed)
        => new(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = allowed, Timeout = TimeSpan.FromSeconds(1), MaxArgumentsBytes = 1000, MaxResultBytes = 1000 });

    private static AiCtx MakeCtx(ActuatorHost host)
    {
        var world = new AiWorld(host);
        var agent = new AiAgent(MakeBareBrain());
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host, new LiveWorldBb(world.Bb));
    }

    private static HfsmInstance MakeBareBrain()
    {
        var g = new HfsmGraph { Root = new StateId("root") };
        g.Add(new StateId("root"), static _ => Empty());
        return new HfsmInstance(g);
    }

    private static IEnumerator<AiStep> Empty()
    {
        yield break;
    }

    private sealed class RecordingInvoker(Func<string, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<string>> impl)
        : ISemanticKernelFunctionInvoker
    {
        public List<(string PluginName, string FunctionName, IReadOnlyDictionary<string, object?> Arguments)> Calls { get; } = [];

        public Task<string> InvokeAsync(string pluginName, string functionName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls.Add((pluginName, functionName, arguments));
            return impl(pluginName, functionName, arguments, cancellationToken);
        }
    }

    private sealed class FakeMetadataReader(IReadOnlyDictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?> byFunction)
        : ISemanticKernelFunctionMetadataReader
    {
        public bool TryGetMetadata(string pluginName, string functionName, out SemanticKernelResolvedFunctionMetadata? metadata)
        {
            if (byFunction.TryGetValue((pluginName, functionName), out var match))
            {
                metadata = match;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
