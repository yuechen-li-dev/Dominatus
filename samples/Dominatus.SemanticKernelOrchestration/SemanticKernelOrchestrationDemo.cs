using Dominatus.Actuators.SemanticKernel;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
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

public sealed record WorkerInstruction(int Sequence, string TargetRole, string Kind, string Subject, string Payload);
public sealed record WorkerReport(int Sequence, string SourceRole, string Kind, string Subject, string Payload);
public sealed record DemoResult(bool Completed, string FinalReport, int TickCount, int ResearchCalls, int ComputeCalls, int WriterCalls, IReadOnlyDictionary<string, string> Ledger, IReadOnlyList<string> AllowedFunctions, IReadOnlyList<string> Events);

public static class SemanticKernelOrchestrationDemo
{
    internal static class Keys
    {
        public static readonly BbKey<string> FactsTinyNet = new("FactsTinyNet");
        public static readonly BbKey<string> FactsVisionMax = new("FactsVisionMax");
        public static readonly BbKey<string> FactsLlamaCalc = new("FactsLlamaCalc");
        public static readonly BbKey<string> DerivedComparison = new("DerivedComparison");
        public static readonly BbKey<string> FinalReport = new("FinalReport");
        public static readonly BbKey<bool> TaskComplete = new("TaskComplete");

        public static readonly BbKey<int> InstructionSequence = new("InstructionSequence");
        public static readonly BbKey<int> CurrentInstructionSequence = new("CurrentInstructionSequence");
        public static readonly BbKey<string> ExpectedReportKind = new("ExpectedReportKind");
        public static readonly BbKey<string> ExpectedReportSubject = new("ExpectedReportSubject");
        public static readonly BbKey<string> CurrentInstructionTarget = new("CurrentInstructionTarget");

        public static readonly BbKey<ActuationId> LastAct = new("LastAct");
        public static readonly BbKey<int> ResearchCalls = new("ResearchCalls");
        public static readonly BbKey<int> ComputeCalls = new("ComputeCalls");
        public static readonly BbKey<int> WriterCalls = new("WriterCalls");
        public static readonly BbKey<int> ResearchTinyNetCount = new("ResearchTinyNetCount");
        public static readonly BbKey<int> ResearchVisionMaxCount = new("ResearchVisionMaxCount");
        public static readonly BbKey<int> ResearchLlamaCalcCount = new("ResearchLlamaCalcCount");
        public static readonly DecisionSlot NextActionSlot = new("SemanticKernelDemo.NextAction");
    }

    public static DemoResult Run(TextWriter? output = null, bool trace = false)
    {
        output ??= TextWriter.Null;
        var events = new List<string>();

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
        foreach (var f in catalog) output.WriteLine($"[capabilities] {f.PluginName}.{f.FunctionName} exists={f.ExistsInKernel}");

        var orchestrator = new AiAgent(new HfsmInstance(Graphs.Orchestrator(output, events), new HfsmOptions { KeepRootFrame = true }) { Trace = trace ? new TextWriterTraceSink(output) : null });
        world.Add(orchestrator);
        world.Add(new AiAgent(new HfsmInstance(Graphs.Worker("research", output, events), new HfsmOptions { KeepRootFrame = true })));
        world.Add(new AiAgent(new HfsmInstance(Graphs.Worker("compute", output, events), new HfsmOptions { KeepRootFrame = true })));
        world.Add(new AiAgent(new HfsmInstance(Graphs.Worker("writer", output, events), new HfsmOptions { KeepRootFrame = true })));

        int ticks = 0;
        for (; ticks < 200 && !world.Bb.GetOrDefault(Keys.TaskComplete, false); ticks++) world.Tick(0.1f);
        if (!world.Bb.GetOrDefault(Keys.TaskComplete, false)) output.WriteLine("[orchestrator] max tick guard reached");

        var final = world.Bb.GetOrDefault(Keys.FinalReport, string.Empty);
        output.WriteLine("\n=== Final Report ===");
        output.WriteLine(final);

        var ledger = new Dictionary<string, string>
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

        return new(world.Bb.GetOrDefault(Keys.TaskComplete, false), final, ticks,
            world.Bb.GetOrDefault(Keys.ResearchCalls, 0), world.Bb.GetOrDefault(Keys.ComputeCalls, 0), world.Bb.GetOrDefault(Keys.WriterCalls, 0),
            ledger, catalog.Select(x => $"{x.PluginName}.{x.FunctionName}").ToArray(), events);
    }

    public static bool IsReportMatch(WorkerReport report, int seq, string kind, string subject)
        => report.Sequence == seq && report.Kind == kind && report.Subject == subject;

    static class Graphs
    {
        public static HfsmGraph Orchestrator(TextWriter output, List<string> events)
        {
            var g = new HfsmGraph { Root = "Root" };
            g.Add(new() { Id = "Root", Node = _ => Root() });
            g.Add(new() { Id = "Initialize", Node = _ => Initialize(output) });
            g.Add(new() { Id = "Loop", Node = ctx => Loop(ctx) });
            g.Add(new() { Id = "AssignResearch", Node = ctx => AssignResearch(ctx, output, events) });
            g.Add(new() { Id = "AssignCompute", Node = ctx => AssignCompute(ctx, output, events) });
            g.Add(new() { Id = "AssignWrite", Node = ctx => AssignWrite(ctx, output, events) });
            g.Add(new() { Id = "AwaitReport", Node = ctx => AwaitReport(ctx, output, events) });
            g.Add(new() { Id = "Complete", Node = _ => Complete() });
            return g;
        }

        public static HfsmGraph Worker(string role, TextWriter output, List<string> events)
        {
            var g = new HfsmGraph { Root = "Root" };
            g.Add(new() { Id = "Root", Node = ctx => WorkLoop(ctx, role, output, events) });
            return g;
        }

        static IEnumerator<AiStep> Root() { yield return Ai.Push("Initialize"); yield return Ai.Steady("parked"); }
        static IEnumerator<AiStep> Initialize(TextWriter output) { output.WriteLine("[orchestrator] initialized task ledger"); yield return Ai.Goto("Loop"); }

        static IEnumerator<AiStep> Loop(AiCtx ctx)
        {
            while (true)
            {
                yield return Ai.Decide(Keys.NextActionSlot,
                [
                    Ai.Option("complete", Utility.Bool((w, _) => HasFinal(w.Bb)), "Complete"),
                    Ai.Option("research", Utility.Bool((w, _) => NeedsResearch(w.Bb)), "AssignResearch"),
                    Ai.Option("compute", Utility.Bool((w, _) => NeedsCompute(w.Bb)), "AssignCompute"),
                    Ai.Option("write", Utility.Bool((w, _) => NeedsWrite(w.Bb)), "AssignWrite"),
                ], hysteresis: 0f, minCommitSeconds: 0f);

                if (HasFinal(ctx.World.Bb)) yield return Ai.Goto("Complete");
                else if (NeedsResearch(ctx.World.Bb)) yield return Ai.Goto("AssignResearch");
                else if (NeedsCompute(ctx.World.Bb)) yield return Ai.Goto("AssignCompute");
                else if (NeedsWrite(ctx.World.Bb)) yield return Ai.Goto("AssignWrite");
                else yield return Ai.Wait(0.1f);
            }
        }

        static IEnumerator<AiStep> AssignResearch(AiCtx ctx, TextWriter output, List<string> events)
        {
            var subject = NextModel(ctx.World.Bb);
            var seq = NextSeq(ctx.World.Bb);
            SetExpected(ctx.World.Bb, seq, "research", "facts", subject);
            events.Add($"assigned research {subject} seq={seq}");
            output.WriteLine($"[orchestrator] next action: research {subject}");
            ctx.Mail.Broadcast(_ => true, new WorkerInstruction(seq, "research", "facts", subject, subject));
            yield return Ai.Goto("AwaitReport");
        }

        static IEnumerator<AiStep> AssignCompute(AiCtx ctx, TextWriter output, List<string> events)
        {
            var seq = NextSeq(ctx.World.Bb);
            SetExpected(ctx.World.Bb, seq, "compute", "comparison", "models");
            events.Add($"assigned compute models seq={seq}");
            output.WriteLine("[orchestrator] next action: compute models");
            ctx.Mail.Broadcast(_ => true, new WorkerInstruction(seq, "compute", "comparison", "models", Facts(ctx.World.Bb)));
            yield return Ai.Goto("AwaitReport");
        }

        static IEnumerator<AiStep> AssignWrite(AiCtx ctx, TextWriter output, List<string> events)
        {
            var seq = NextSeq(ctx.World.Bb);
            SetExpected(ctx.World.Bb, seq, "writer", "report", "final");
            events.Add($"assigned writer final seq={seq}");
            output.WriteLine("[orchestrator] next action: writer final");
            ctx.Mail.Broadcast(_ => true, new WorkerInstruction(seq, "writer", "report", "final", Ledger(ctx.World.Bb)));
            yield return Ai.Goto("AwaitReport");
        }

        static IEnumerator<AiStep> AwaitReport(AiCtx ctx, TextWriter output, List<string> events)
        {
            var seq = ctx.World.Bb.GetOrDefault(Keys.CurrentInstructionSequence, 0);
            var kind = ctx.World.Bb.GetOrDefault(Keys.ExpectedReportKind, string.Empty);
            var subject = ctx.World.Bb.GetOrDefault(Keys.ExpectedReportSubject, string.Empty);

            WorkerReport? accepted = null;
            yield return Ai.Event<WorkerReport>(filter: r => IsReportMatch(r, seq, kind, subject), onConsumed: (_, report) => accepted = report);
            if (accepted is null) yield return Ai.Goto("Loop");

            output.WriteLine($"[{accepted!.SourceRole}] report: {accepted.Payload}");
            events.Add($"consumed report {accepted.Kind} {accepted.Subject} seq={accepted.Sequence}");
            ApplyReport(ctx.World.Bb, accepted);
            yield return Ai.Goto("Loop");
        }

        static IEnumerator<AiStep> Complete() { yield return Ai.Steady("complete"); }

        static IEnumerator<AiStep> WorkLoop(AiCtx ctx, string role, TextWriter output, List<string> events)
        {
            var lastSeqKey = new BbKey<int>($"LastProcessedInstructionSequence.{role}");
            while (true)
            {
                WorkerInstruction? instruction = null;
                yield return Ai.Event<WorkerInstruction>(
                    filter: m => m.TargetRole == role && m.Sequence > ctx.Agent.Bb.GetOrDefault(lastSeqKey, 0),
                    onConsumed: (a, msg) => { instruction = msg; a.Bb.Set(lastSeqKey, msg.Sequence); });

                events.Add($"{role} consumed instruction {instruction!.Kind} {instruction.Subject} seq={instruction.Sequence}");

                var function = role == "research" ? "lookup_model_facts" : role == "compute" ? "compare_efficiency" : "write_report";
                var args = role == "research"
                    ? $"{{\"modelName\":\"{instruction.Subject}\"}}"
                    : role == "compute"
                        ? $"{{\"factsText\":\"{Facts(ctx.World.Bb)}\"}}"
                        : $"{{\"ledgerText\":\"{Ledger(ctx.World.Bb)}\"}}";

                output.WriteLine($"[{role}] SK {role}.{function}(...)");
                yield return Ai.Act(new SemanticKernelFunctionCommand(role, function, args), Keys.LastAct);
                var resultKey = new BbKey<SemanticKernelFunctionResult>($"Result.{role}");
                yield return Ai.Await<SemanticKernelFunctionResult>(Keys.LastAct, resultKey);
                var result = ctx.Agent.Bb.GetOrDefault(resultKey, null!);

                var report = new WorkerReport(instruction.Sequence, role, instruction.Kind, instruction.Subject, result.ResultText);
                events.Add($"{role} reported {report.Kind} {report.Subject} seq={report.Sequence}");
                ctx.Mail.Broadcast(_ => true, report);
            }
        }

        static int NextSeq(Blackboard bb)
        {
            var next = bb.GetOrDefault(Keys.InstructionSequence, 0) + 1;
            bb.Set(Keys.InstructionSequence, next);
            return next;
        }

        static void SetExpected(Blackboard bb, int seq, string target, string kind, string subject)
        {
            bb.Set(Keys.CurrentInstructionSequence, seq);
            bb.Set(Keys.CurrentInstructionTarget, target);
            bb.Set(Keys.ExpectedReportKind, kind);
            bb.Set(Keys.ExpectedReportSubject, subject);
        }
    }

    static void ApplyReport(Blackboard bb, WorkerReport report)
    {
        if (report.Kind == "facts")
        {
            bb.Set(Keys.ResearchCalls, bb.GetOrDefault(Keys.ResearchCalls, 0) + 1);
            if (report.Subject == "TinyNet") { bb.Set(Keys.FactsTinyNet, report.Payload); bb.Set(Keys.ResearchTinyNetCount, bb.GetOrDefault(Keys.ResearchTinyNetCount, 0) + 1); }
            else if (report.Subject == "VisionMax") { bb.Set(Keys.FactsVisionMax, report.Payload); bb.Set(Keys.ResearchVisionMaxCount, bb.GetOrDefault(Keys.ResearchVisionMaxCount, 0) + 1); }
            else if (report.Subject == "LlamaCalc") { bb.Set(Keys.FactsLlamaCalc, report.Payload); bb.Set(Keys.ResearchLlamaCalcCount, bb.GetOrDefault(Keys.ResearchLlamaCalcCount, 0) + 1); }
            return;
        }

        if (report.Kind == "comparison")
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
