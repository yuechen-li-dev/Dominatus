using Dominatus.Actuators.SemanticKernel;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;
using Dominatus.UtilityLite;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace Dominatus.SemanticKernelOrchestration;

public sealed record WorkerInstruction(string TargetRole, string Kind, string Payload, int Sequence);
public sealed record WorkerReport(string SourceRole, string Kind, string Subject, string Payload);
public sealed record DemoResult(bool Completed, string FinalReport, int TickCount, int ResearchCalls, int ComputeCalls, int WriterCalls, IReadOnlyDictionary<string, string> Ledger, IReadOnlyList<string> AllowedFunctions);

public static class SemanticKernelOrchestrationDemo
{
    static class Keys
    {
        public static readonly BbKey<string> FactsTinyNet = new("FactsTinyNet");
        public static readonly BbKey<string> FactsVisionMax = new("FactsVisionMax");
        public static readonly BbKey<string> FactsLlamaCalc = new("FactsLlamaCalc");
        public static readonly BbKey<string> DerivedComparison = new("DerivedComparison");
        public static readonly BbKey<string> FinalReport = new("FinalReport");
        public static readonly BbKey<bool> TaskComplete = new("TaskComplete");
        public static readonly BbKey<ActuationId> LastAct = new("LastAct");
        public static readonly BbKey<int> ResearchCalls = new("ResearchCalls");
        public static readonly BbKey<int> ComputeCalls = new("ComputeCalls");
        public static readonly BbKey<int> WriterCalls = new("WriterCalls");
        public static readonly BbKey<int> ResearchTinyNetCount = new("ResearchTinyNetCount");
        public static readonly BbKey<int> ResearchVisionMaxCount = new("ResearchVisionMaxCount");
        public static readonly BbKey<int> ResearchLlamaCalcCount = new("ResearchLlamaCalcCount");
        public static readonly BbKey<int> InstructionSequence = new("InstructionSequence");
    }

    public static DemoResult Run(TextWriter? output = null, bool trace = false)
    {
        output ??= TextWriter.Null;
        var kernel = Kernel.CreateBuilder().Build();
        kernel.Plugins.AddFromObject(new DemoResearchPlugin(), "research");
        kernel.Plugins.AddFromObject(new DemoComputePlugin(), "compute");
        kernel.Plugins.AddFromObject(new DemoWriterPlugin(), "writer");

        var options = new SemanticKernelActuatorOptions { AllowedFunctions = [new("research", "lookup_model_facts"), new("compute", "compare_efficiency"), new("writer", "write_report")] };
        var catalog = new SemanticKernelFunctionCatalog(kernel, options).GetAllowedFunctions();

        var host = new ActuatorHost();
        host.RegisterSemanticKernelActuators(kernel, options);
        var world = new AiWorld(host);

        output.WriteLine("=== Dominatus Semantic Kernel Orchestration Demo ===");
        foreach (var f in catalog)
            output.WriteLine($"[capabilities] {f.PluginName}.{f.FunctionName} exists={f.ExistsInKernel}");

        var orchestrator = new AiAgent(new HfsmInstance(Graphs.Orchestrator(output), new HfsmOptions { KeepRootFrame = true }) { Trace = trace ? new TextWriterTraceSink(output) : null });
        var research = new AiAgent(new HfsmInstance(Graphs.Worker("research", output), new HfsmOptions { KeepRootFrame = true }));
        var compute = new AiAgent(new HfsmInstance(Graphs.Worker("compute", output), new HfsmOptions { KeepRootFrame = true }));
        var writer = new AiAgent(new HfsmInstance(Graphs.Worker("writer", output), new HfsmOptions { KeepRootFrame = true }));
        world.Add(orchestrator); world.Add(research); world.Add(compute); world.Add(writer);

        int ticks = 0;
        for (; ticks < 200 && !world.Bb.GetOrDefault(Keys.TaskComplete, false); ticks++) world.Tick(0.1f);

        if (!world.Bb.GetOrDefault(Keys.TaskComplete, false)) output.WriteLine("[orchestrator] max tick guard reached");

        var final = world.Bb.GetOrDefault(Keys.FinalReport, string.Empty);
        output.WriteLine("\n=== Final Report ===");
        output.WriteLine(final);

        var ledger = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FactsTinyNet"] = world.Bb.GetOrDefault(Keys.FactsTinyNet, string.Empty),
            ["FactsVisionMax"] = world.Bb.GetOrDefault(Keys.FactsVisionMax, string.Empty),
            ["FactsLlamaCalc"] = world.Bb.GetOrDefault(Keys.FactsLlamaCalc, string.Empty),
            ["DerivedComparison"] = world.Bb.GetOrDefault(Keys.DerivedComparison, string.Empty),
            ["FinalReport"] = final,
            ["TaskComplete"] = world.Bb.GetOrDefault(Keys.TaskComplete, false).ToString().ToLowerInvariant(),
            ["ResearchTinyNetCount"] = world.Bb.GetOrDefault(Keys.ResearchTinyNetCount, 0).ToString(),
            ["ResearchVisionMaxCount"] = world.Bb.GetOrDefault(Keys.ResearchVisionMaxCount, 0).ToString(),
            ["ResearchLlamaCalcCount"] = world.Bb.GetOrDefault(Keys.ResearchLlamaCalcCount, 0).ToString(),
        };

        return new(
            world.Bb.GetOrDefault(Keys.TaskComplete, false),
            final,
            ticks,
            world.Bb.GetOrDefault(Keys.ResearchCalls, 0),
            world.Bb.GetOrDefault(Keys.ComputeCalls, 0),
            world.Bb.GetOrDefault(Keys.WriterCalls, 0),
            ledger,
            catalog.Select(x => $"{x.PluginName}.{x.FunctionName}").ToArray());
    }

    static class Graphs
    {
        public static HfsmGraph Orchestrator(TextWriter output)
        {
            var g = new HfsmGraph { Root = "Root" };
            g.Add(new() { Id = "Root", Node = _ => Root() });
            g.Add(new() { Id = "Initialize", Node = _ => Initialize(output) });
            g.Add(new() { Id = "Loop", Node = ctx => Loop(ctx) });
            g.Add(new() { Id = "AssignResearch", Node = ctx => Assign(ctx, "research", NextModel(ctx.World.Bb), output) });
            g.Add(new() { Id = "AssignCompute", Node = ctx => Assign(ctx, "compute", "all-facts", output) });
            g.Add(new() { Id = "AssignWrite", Node = ctx => Assign(ctx, "writer", "final-report", output) });
            g.Add(new() { Id = "AwaitReport", Node = ctx => AwaitReport(ctx, output) });
            g.Add(new() { Id = "Complete", Node = _ => Complete() });
            g.Add(new() { Id = "Stalled", Node = _ => Stalled() });
            return g;
        }

        public static HfsmGraph Worker(string role, TextWriter output)
        {
            var g = new HfsmGraph { Root = "Root" };
            g.Add(new() { Id = "Root", Node = ctx => WorkLoop(ctx, role, output) });
            return g;
        }

        static IEnumerator<AiStep> Root() { yield return Ai.Push("Initialize"); yield return Ai.Steady("parked"); }
        static IEnumerator<AiStep> Initialize(TextWriter output) { output.WriteLine("[orchestrator] initialized task ledger"); yield return Ai.Goto("Loop"); }

        static IEnumerator<AiStep> Loop(AiCtx ctx)
        {
            yield return Ai.Decide([
                Ai.Option("complete", Utility.Bool((w, _) => HasFinal(w.Bb)), "Complete"),
                Ai.Option("research", Utility.Bool((w, _) => NeedsResearch(w.Bb)), "AssignResearch"),
                Ai.Option("compute", Utility.Bool((w, _) => NeedsCompute(w.Bb)), "AssignCompute"),
                Ai.Option("write", Utility.Bool((w, _) => NeedsWrite(w.Bb)), "AssignWrite"),
                Ai.Option("stall", Utility.Bool((_, _) => false), "Stalled"),
            ], minCommitSeconds: 0f);
        }

        static IEnumerator<AiStep> Assign(AiCtx ctx, string role, string payload, TextWriter output)
        {
            output.WriteLine($"[orchestrator] next action: {role} {payload}");
            var seq = ctx.World.Bb.GetOrDefault(Keys.InstructionSequence, 0) + 1;
            ctx.World.Bb.Set(Keys.InstructionSequence, seq);
            ctx.Mail.Broadcast(_ => true, new WorkerInstruction(role, "work", payload, seq));
            yield return Ai.Goto("AwaitReport");
        }

        static IEnumerator<AiStep> AwaitReport(AiCtx ctx, TextWriter output)
        {
            yield return Ai.Event<WorkerReport>(filter: r => r.Kind == "done", onConsumed: (_, report) =>
            {
                output.WriteLine($"[{report.SourceRole}] report: {report.Payload}");
                ApplyReport(ctx.World.Bb, report);
            });
            yield return Ai.Goto("Loop");
        }

        static IEnumerator<AiStep> Complete() { yield return Ai.Steady("complete"); }
        static IEnumerator<AiStep> Stalled() { yield return Ai.Fail("stalled"); }

        static IEnumerator<AiStep> WorkLoop(AiCtx ctx, string role, TextWriter output)
        {
            while (true)
            {
                WorkerInstruction? instruction = null;
                var lastSeqKey = new BbKey<int>($"LastSequence.{role}");
                yield return Ai.Event<WorkerInstruction>(
                    filter: m => m.TargetRole == role && m.Sequence > ctx.Agent.Bb.GetOrDefault(lastSeqKey, 0),
                    onConsumed: (a, msg) => { instruction = msg; a.Bb.Set(lastSeqKey, msg.Sequence); });
                yield return Ai.Wait(0.1f);

                string function = role switch
                {
                    "research" => "lookup_model_facts",
                    "compute" => "compare_efficiency",
                    _ => "write_report"
                };

                string args = role switch
                {
                    "research" => $"{{\"modelName\":\"{instruction!.Payload}\"}}",
                    "compute" => $"{{\"factsText\":\"{Facts(ctx.World.Bb)}\"}}",
                    _ => $"{{\"ledgerText\":\"{Ledger(ctx.World.Bb)}\"}}"
                };

                output.WriteLine($"[{role}] SK {role}.{function}(...)");
                yield return Ai.Act(new SemanticKernelFunctionCommand(role, function, args), Keys.LastAct);
                yield return Ai.Await<SemanticKernelFunctionResult>(Keys.LastAct, new BbKey<SemanticKernelFunctionResult>("Result"));

                var result = ctx.Agent.Bb.GetOrDefault(new BbKey<SemanticKernelFunctionResult>("Result"), null!);
                var subject = role == "research" ? instruction!.Payload : role;
                ctx.Mail.Broadcast(_ => true, new WorkerReport(role, "done", subject, result.ResultText));
            }
        }
    }

    static void ApplyReport(Blackboard bb, WorkerReport report)
    {
        if (report.SourceRole == "research")
        {
            bb.Set(Keys.ResearchCalls, bb.GetOrDefault(Keys.ResearchCalls, 0) + 1);
            if (report.Subject == "TinyNet") { bb.Set(Keys.FactsTinyNet, report.Payload); bb.Set(Keys.ResearchTinyNetCount, bb.GetOrDefault(Keys.ResearchTinyNetCount, 0) + 1); }
            else if (report.Subject == "VisionMax") { bb.Set(Keys.FactsVisionMax, report.Payload); bb.Set(Keys.ResearchVisionMaxCount, bb.GetOrDefault(Keys.ResearchVisionMaxCount, 0) + 1); }
            else { bb.Set(Keys.FactsLlamaCalc, report.Payload); bb.Set(Keys.ResearchLlamaCalcCount, bb.GetOrDefault(Keys.ResearchLlamaCalcCount, 0) + 1); }
            return;
        }

        if (report.SourceRole == "compute")
        {
            bb.Set(Keys.ComputeCalls, bb.GetOrDefault(Keys.ComputeCalls, 0) + 1);
            bb.Set(Keys.DerivedComparison, report.Payload);
            return;
        }

        bb.Set(Keys.WriterCalls, bb.GetOrDefault(Keys.WriterCalls, 0) + 1);
        bb.Set(Keys.FinalReport, report.Payload);
        bb.Set(Keys.TaskComplete, true);
    }

    static bool HasFinal(Blackboard bb) => !string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.FinalReport, string.Empty));
    static bool NeedsResearch(Blackboard bb) => string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.FactsTinyNet, string.Empty)) || string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.FactsVisionMax, string.Empty)) || string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.FactsLlamaCalc, string.Empty));
    static bool NeedsCompute(Blackboard bb) => !NeedsResearch(bb) && string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.DerivedComparison, string.Empty));
    static bool NeedsWrite(Blackboard bb) => !string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.DerivedComparison, string.Empty)) && string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.FinalReport, string.Empty));
    static string NextModel(Blackboard bb) => string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.FactsTinyNet, string.Empty)) ? "TinyNet" : string.IsNullOrWhiteSpace(bb.GetOrDefault(Keys.FactsVisionMax, string.Empty)) ? "VisionMax" : "LlamaCalc";
    static string Facts(Blackboard bb) => $"{bb.GetOrDefault(Keys.FactsTinyNet, string.Empty)} | {bb.GetOrDefault(Keys.FactsVisionMax, string.Empty)} | {bb.GetOrDefault(Keys.FactsLlamaCalc, string.Empty)}";
    static string Ledger(Blackboard bb) => $"{Facts(bb)} :: {bb.GetOrDefault(Keys.DerivedComparison, string.Empty)}";
}

public sealed class DemoResearchPlugin
{
    [KernelFunction("lookup_model_facts"), Description("Returns deterministic model energy and CO2 facts.")]
    public string LookupModelFacts(string modelName) => modelName switch
    {
        "TinyNet" => "TinyNet: 0.4 kWh/run, 0.16 kg CO2/run",
        "VisionMax" => "VisionMax: 2.8 kWh/run, 1.12 kg CO2/run",
        _ => "LlamaCalc: 1.1 kWh/run, 0.44 kg CO2/run"
    };
}

public sealed class DemoComputePlugin
{
    [KernelFunction("compare_efficiency")]
    public string CompareEfficiency(string factsText) => "TinyNet is lowest energy / lowest CO2, VisionMax is highest, and LlamaCalc is in the middle.";
}

public sealed class DemoWriterPlugin
{
    [KernelFunction("write_report")]
    public string WriteReport(string ledgerText) => "TinyNet is the most efficient in this toy comparison on both energy use and CO2 emissions. VisionMax is the least efficient, while LlamaCalc is in the middle.";
}
