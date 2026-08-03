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

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<string> Generated) Run(string source)
    {
        var compilation = CSharpCompilation.Create("GeneratedFlowTests", [CSharpSyntaxTree.ParseText(source)], References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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
