using Dominatus.Core.Nodes;
using Dominatus.OptFlow;
using Dominatus.OptFlow.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Dominatus.OptFlow.Generators.Tests;

public sealed class OptFlowGeneratorTests
{
    [Fact]
    public void StaticFlow_GeneratesCompilableDeterministicOrdinaryOptFlow()
    {
        var result = Run("""
            using System.Collections.Generic;
            using Dominatus.Core.Nodes;
            using Dominatus.Core.Runtime;
            using Dominatus.OptFlow;
            public static partial class Sample {
              [DominatusFlow("sample.flow", KeepRootFrame = true)] public static partial FlowDefinition Define();
              [DominatusState("zeta")] private static IEnumerator<AiStep> Zed(AiCtx ctx) { yield return Ai.Succeed(); }
              [DominatusState("root", Root = true)] private static IEnumerator<AiStep> Entry(AiCtx ctx) { yield return Ai.Goto(States.Zed); }
              [DominatusState("alpha")] private static IEnumerator<AiStep> Alpha(AiCtx ctx) { yield return Ai.Succeed(); }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var generated = Assert.Single(result.Generated);
        Assert.Contains("global::Dominatus.OptFlow.Flow.State", generated);
        Assert.Contains("global::Dominatus.OptFlow.Flow.Define", generated);
        Assert.Contains("StateId.Of(\"root\")", generated);
        Assert.True(generated.IndexOf("__state_Entry", StringComparison.Ordinal) < generated.IndexOf("__state_Alpha", StringComparison.Ordinal));
        Assert.True(generated.IndexOf("__state_Alpha", StringComparison.Ordinal) < generated.IndexOf("__state_Zed", StringComparison.Ordinal));
        Assert.DoesNotContain("NoSuchBodyTarget", generated);
    }

    [Fact]
    public void ParameterizedFactory_CapturesOnlyFullyMatchedStateParameters()
    {
        var result = Run("""
            using System.Collections.Generic;
            using Dominatus.Core.Nodes;
            using Dominatus.Core.Runtime;
            using Dominatus.OptFlow;
            public static partial class Sample {
              [DominatusFlow("sample.parameterized")] public static partial FlowDefinition Define(int policy);
              [DominatusState("Root", Root = true)] private static IEnumerator<AiStep> Root(AiCtx ctx, int policy) { yield return Ai.Succeed(policy.ToString()); }
              [DominatusState("Leaf")] private static IEnumerator<AiStep> Leaf(AiCtx ctx) { yield return Ai.Succeed(); }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ctx => Root(ctx, policy)", Assert.Single(result.Generated));
        Assert.Contains("States.Leaf, Leaf", Assert.Single(result.Generated));
    }

    [Fact]
    public void PartialFilesAndSyntaxTreeOrder_ProduceIdenticalDeterministicSource()
    {
        const string factoryAndRoot = """
            using System.Collections.Generic;
            using Dominatus.Core.Nodes;
            using Dominatus.Core.Runtime;
            using Dominatus.OptFlow;
            public static partial class Sample {
              [DominatusFlow("stable.flow")] public static partial FlowDefinition Define();
              [DominatusState("Root", Root = true)] private static IEnumerator<AiStep> Root(AiCtx ctx) { yield return Ai.Succeed(); }
            }
            """;
        const string leaves = """
            using System.Collections.Generic;
            using Dominatus.Core.Nodes;
            using Dominatus.Core.Runtime;
            using Dominatus.OptFlow;
            public static partial class Sample {
              [DominatusState("z")] private static IEnumerator<AiStep> Z(AiCtx ctx) { yield return Ai.Succeed(); }
              [DominatusState("a")] private static IEnumerator<AiStep> A(AiCtx ctx) { yield return Ai.Succeed(); }
            }
            """;

        var forward = Run(factoryAndRoot, leaves);
        var reverse = Run(leaves, factoryAndRoot);
        Assert.Equal(Assert.Single(forward.Generated), Assert.Single(reverse.Generated));
        Assert.Contains("__state_Root, __state_A, __state_Z", Assert.Single(forward.Generated));
    }

    [Theory]
    [InlineData("[DominatusFlow(\" \")]", "DOMFLOW014")]
    [InlineData("[DominatusFlow(\"x\")]", "DOMFLOW005")]
    public void InvalidAuthoring_ReportsStableDiagnostics(string flowAttribute, string diagnosticId)
    {
        var states = diagnosticId == "DOMFLOW005" ? "[DominatusState(\"Leaf\")] private static IEnumerator<AiStep> Leaf(AiCtx ctx) { yield return Ai.Succeed(); }" : "[DominatusState(\"Root\", Root = true)] private static IEnumerator<AiStep> Root(AiCtx ctx) { yield return Ai.Succeed(); }";
        var result = Run($$"""
            using System.Collections.Generic;
            using Dominatus.Core.Nodes;
            using Dominatus.Core.Runtime;
            using Dominatus.OptFlow;
            public static partial class Sample {
              {{flowAttribute}} public static partial FlowDefinition Define();
              {{states}}
            }
            """);
        Assert.Contains(result.Diagnostics, d => d.Id == diagnosticId);
    }

    [Fact]
    public void OverloadedAnnotatedStates_AreRejected()
    {
        var result = Run("""
            using System.Collections.Generic;
            using Dominatus.Core.Nodes;
            using Dominatus.Core.Runtime;
            using Dominatus.OptFlow;
            public static partial class Sample {
              [DominatusFlow("sample")] public static partial FlowDefinition Define();
              [DominatusState("Root", Root = true)] private static IEnumerator<AiStep> State(AiCtx ctx) { yield return Ai.Succeed(); }
              [DominatusState("Other")] private static IEnumerator<AiStep> State(AiCtx ctx, int ignored) { yield return Ai.Succeed(); }
            }
            """);
        Assert.Contains(result.Diagnostics, d => d.Id == "DOMFLOW011");
    }

    [Theory]
    [MemberData(nameof(PublicDiagnosticCases))]
    public void EveryPublicDiagnostic_IsStableAndDoesNotEmitPartialOutput(string expectedId, string source)
    {
        var result = Run(source);
        var diagnostic = result.Diagnostics.First(d => d.Id == expectedId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "AD0001");
        Assert.Empty(result.Generated);
    }

    public static IEnumerable<object[]> PublicDiagnosticCases()
    {
        const string root = "[DominatusState(\"Root\", Root = true)] private static IEnumerator<AiStep> Root(AiCtx ctx) { yield return Ai.Succeed(); }";
        const string leaf = "[DominatusState(\"Leaf\")] private static IEnumerator<AiStep> Leaf(AiCtx ctx) { yield return Ai.Succeed(); }";
        yield return Case("DOMFLOW001", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static FlowDefinition Define() => null!;", root));
        yield return Case("DOMFLOW002", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial int Define();", root));
        yield return Case("DOMFLOW003", Source("public partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", root));
        yield return Case("DOMFLOW004", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", ""));
        yield return Case("DOMFLOW005", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", leaf));
        yield return Case("DOMFLOW006", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", root + leaf.Replace("[DominatusState(\"Leaf\")]", "[DominatusState(\"Leaf\", Root = true)]")));
        yield return Case("DOMFLOW007", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", root + leaf.Replace("\"Leaf\"", "\"Root\"")));
        yield return Case("DOMFLOW008", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", "[DominatusState(\"Root\", Root = true)] private static int Root(AiCtx ctx) => 0;"));
        yield return Case("DOMFLOW009", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define(ref int value);", root));
        yield return Case("DOMFLOW010", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", "[DominatusState(\"Root\", Root = true)] private static IEnumerator<AiStep> Root<T>(AiCtx ctx) { yield return Ai.Succeed(); }"));
        yield return Case("DOMFLOW011", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", root + "[DominatusState(\"Other\")] private static IEnumerator<AiStep> Root(AiCtx ctx, int x) { yield return Ai.Succeed(); }"));
        yield return Case("DOMFLOW012", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", "public static class States {}" + root));
        yield return Case("DOMFLOW013", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define(); [DominatusFlow(\"y\")] public static partial FlowDefinition Other();", root));
        yield return Case("DOMFLOW014", Source("public static partial class Sample", "[DominatusFlow(\" \")] public static partial FlowDefinition Define();", root));
        yield return Case("DOMFLOW015", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define(int value);", "[DominatusState(\"Root\", Root = true)] private static IEnumerator<AiStep> Root(AiCtx ctx, string value) { yield return Ai.Succeed(); }"));
        yield return Case("DOMFLOW016", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public partial FlowDefinition Define();", root));
        yield return Case("DOMFLOW017", Source("public partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", "[DominatusState(\"Root\", Root = true)] private IEnumerator<AiStep> Root(AiCtx ctx) { yield return Ai.Succeed(); }"));
        yield return Case("DOMFLOW018", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", root + "[DominatusState(\"Other\")] private static IEnumerator<AiStep> Root(AiCtx ctx, int x) { yield return Ai.Succeed(); }"));
        yield return Case("DOMFLOW019", Source("public static partial class Sample<T>", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define();", root));
        yield return Case("DOMFLOW020", Source("public static partial class Sample", "[DominatusFlow(\"x\")] public static partial FlowDefinition Define(); public static partial FlowDefinition Define() => null!;", root));
    }

    private static object[] Case(string id, string source) => [id, source];

    private static string Source(string type, string factory, string states) => $$"""
        using System.Collections.Generic;
        using Dominatus.Core.Nodes;
        using Dominatus.Core.Runtime;
        using Dominatus.OptFlow;
        {{type}} { {{factory}} {{states}} }
        """;

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string> Generated) Run(params string[] sources)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"input-{index}.cs"));
        var compilation = CSharpCompilation.Create("GeneratedFlowTests", trees, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OptFlowGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        var run = driver.GetRunResult();
        return (updated.GetDiagnostics().Concat(run.Diagnostics).ToArray(), run.GeneratedTrees.Select(tree => tree.GetText().ToString()).ToArray());
    }

    private static IEnumerable<MetadataReference> References()
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        foreach (var path in trusted) yield return MetadataReference.CreateFromFile(path);
        yield return MetadataReference.CreateFromFile(typeof(FlowDefinition).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(AiStep).Assembly.Location);
    }
}
