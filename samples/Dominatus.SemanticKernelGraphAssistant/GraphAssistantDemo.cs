using System.Text.Json;
using Dominatus.Actuators.SemanticKernel;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;
using Dominatus.OptFlow;

namespace Dominatus.SemanticKernelGraphAssistant;

public sealed record GraphAssistantDemoResult
{
    public required bool ApprovalGranted { get; init; }
    public required string FinalAction { get; init; }
    public required IReadOnlyList<string> InvokedFunctions { get; init; }
    public required IReadOnlyList<string> DecisionEvents { get; init; }
    public required IReadOnlyDictionary<string, string> BlackboardOutputs { get; init; }
    public required bool SentMail { get; init; }
    public required bool CreatedCalendarEvent { get; init; }
    public required bool CreatedDraft { get; init; }
    public required IReadOnlyList<string> AllowedFunctions { get; init; }
    public required int TickCount { get; init; }
    public required string? DraftText { get; init; }
    public required int LlmCallCount { get; init; }
    public required IReadOnlyList<string> LlmEvents { get; init; }
    public required string? DraftPromptSummary { get; init; }
    public required bool UsedLlmCall { get; init; }
}

public static class GraphAssistantDemo
{
    private static readonly BbKey<string> DraftTextKey = new("GraphAssistant.DraftText");
    private static readonly BbKey<string> DraftJsonKey = new("GraphAssistant.DraftJson");

    public static GraphAssistantDemoResult Run(bool approvalGranted, TextWriter? output = null)
    {
        output ??= TextWriter.Null;

        var profile = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar();
        var modeAllowlist = profile.ToAllowedFunctions(e => e.Risk == SemanticKernelCapabilityRisk.Read).ToList();
        modeAllowlist.Add(new AllowedSemanticKernelFunction("graph.mail", "create_draft"));
        if (approvalGranted)
        {
            modeAllowlist.Add(new AllowedSemanticKernelFunction("graph.mail", "send_message"));
            modeAllowlist.Add(new AllowedSemanticKernelFunction("graph.calendar", "create_event"));
        }

        var fake = new FakeGraphPlugin();
        var fakeLlm = new FakeLlmClient("Thanks for checking in — the deployment is still on track. I'll send a fuller update if anything changes.");
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(fakeLlm, new InMemoryLlmCassette(), LlmCassetteMode.Live));
        host.Register<SemanticKernelFunctionCommand>(new FakeSemanticKernelHandler(fake, modeAllowlist));
        host.AddPolicy(Dominatus.Core.Runtime.ActuationPolicies.Predicate((_, command) =>
        {
            if (command is not SemanticKernelFunctionCommand sk) return true;
            var isGated = (sk.PluginName, sk.FunctionName) is ("graph.mail", "send_message") or ("graph.calendar", "create_event");
            return !isGated || approvalGranted;
        }, "Approval required for send/create actions."));

        var ctx = MakeCtx(host);
        var decisionEvents = new List<string>();
        var outputs = new Dictionary<string, string>();

        outputs["mailHeaders"] = DispatchText(host, ctx, new("graph.mail", "list_messages", "{}"));
        outputs["urgentMessageBody"] = DispatchText(host, ctx, new("graph.mail", "read_message", "{\"id\":\"mail-urgent\"}"));
        outputs["calendarSummary"] = DispatchText(host, ctx, new("graph.calendar", "list_events", "{}"));

        var chosen = DecideNextAction(approvalGranted, createdDraft: false, sentMail: false, createdEvent: false);
        decisionEvents.Add($"Ai.Decide chose {chosen}");

        ExecuteLlmDraftCall(ctx, approvalGranted, fake.UrgentMailSubject, fake.UrgentMailSender, outputs["urgentMessageBody"], outputs["calendarSummary"]);
        var draftText = ctx.Bb.GetOrDefault(DraftTextKey, string.Empty);
        fake.GeneratedDraftText = draftText;

        outputs["draftText"] = draftText;
        outputs["draftJson"] = ctx.Bb.GetOrDefault(DraftJsonKey, string.Empty);
        outputs["draftPromptSummary"] = $"subject={fake.UrgentMailSubject};sender={fake.UrgentMailSender};approval={approvalGranted}";

        if (chosen == "DraftReply")
        {
            outputs["draftResult"] = DispatchText(host, ctx, new("graph.mail", "create_draft", JsonSerializer.Serialize(new { id = "mail-urgent", body = draftText })));
            if (!approvalGranted) decisionEvents.Add("Approval missing; send/create denied or not selected");
        }
        else if (chosen == "SendApprovedReply")
        {
            outputs["sendResult"] = DispatchText(host, ctx, new("graph.mail", "send_message", JsonSerializer.Serialize(new { id = "mail-urgent", body = draftText })));
        }
        else if (chosen == "CreateCalendarEvent")
        {
            outputs["calendarCreateResult"] = DispatchText(host, ctx, new("graph.calendar", "create_event", "{\"id\":\"meeting-next-week\"}"));
        }

        outputs["finalAction"] = "Idle";
        outputs["approvalGranted"] = approvalGranted.ToString().ToLowerInvariant();
        outputs.TryAdd("draftResult", string.Empty);
        outputs.TryAdd("sendResult", string.Empty);
        outputs.TryAdd("calendarCreateResult", string.Empty);

        output.WriteLine($"mode approval={approvalGranted} action={chosen} sent={fake.SentMail} event={fake.CreatedEvent} draft={fake.CreatedDraft} llmCalls={fakeLlm.CallCount}");

        return new GraphAssistantDemoResult
        {
            ApprovalGranted = approvalGranted,
            FinalAction = "Idle",
            InvokedFunctions = fake.Invocations,
            DecisionEvents = decisionEvents,
            BlackboardOutputs = outputs,
            SentMail = fake.SentMail,
            CreatedCalendarEvent = fake.CreatedEvent,
            CreatedDraft = fake.CreatedDraft,
            AllowedFunctions = modeAllowlist.Select(x => $"{x.PluginName}.{x.FunctionName}").ToArray(),
            TickCount = 1,
            DraftText = draftText,
            LlmCallCount = fakeLlm.CallCount,
            LlmEvents = ["Llm.Call graph-assistant.draft-urgent-reply"],
            DraftPromptSummary = outputs["draftPromptSummary"],
            UsedLlmCall = fakeLlm.CallCount > 0,
        };
    }

    private static void ExecuteLlmDraftCall(AiCtx ctx, bool approvalGranted, string urgentSubject, string urgentSender, string urgentBody, string calendarSummary)
    {
        var step = global::Dominatus.Llm.OptFlow.Llm.Call(
            stableId: "graph-assistant.draft-urgent-reply",
            intent: "Draft a concise reply to the urgent customer email confirming deployment status.",
            persona: "Concise professional Outlook assistant.",
            b =>
            {
                b.Add("urgent_email_subject", urgentSubject);
                b.Add("urgent_email_sender", urgentSender);
                b.Add("urgent_email_body", urgentBody);
                b.Add("calendar_summary", calendarSummary);
                b.Add("approval_mode", approvalGranted ? "approved-send" : "draft-only");
            },
            DraftTextKey,
            DraftJsonKey);

        var wait = (IWaitEvent)step;
        var cursor = default(EventCursor);
        if (!wait.TryConsume(ctx, ref cursor))
        {
            wait.TryConsume(ctx, ref cursor);
        }
    }

    private static string DecideNextAction(bool approvalGranted, bool createdDraft, bool sentMail, bool createdEvent)
    {
        static BbKey<bool> Key(string name) => new(name);
        var world = new AiWorld();
        var g = new HfsmGraph { Root = "Root" };
        g.Add(new() { Id = "Root", Node = _ => RootNode() });
        g.Add(new() { Id = "DraftReply", Node = _ => IdleNode() });
        g.Add(new() { Id = "SendApprovedReply", Node = _ => IdleNode() });
        g.Add(new() { Id = "CreateCalendarEvent", Node = _ => IdleNode() });
        g.Add(new() { Id = "Idle", Node = _ => IdleNode() });

        var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        agent.Bb.Set(Key("approvalGranted"), approvalGranted);
        agent.Bb.Set(Key("createdDraft"), createdDraft);
        agent.Bb.Set(Key("sentMail"), sentMail);
        agent.Bb.Set(Key("createdEvent"), createdEvent);

        world.Tick(0.1f);
        world.Tick(0.1f);

        var chosen = brain.GetActivePath().Last().Value;
        return chosen;

        static IEnumerator<AiStep> RootNode()
        {
            static BbKey<bool> K(string name) => new(name);
            yield return Ai.Decide(new DecisionSlot("GraphAssistant.NextAction"),
            [
                Ai.Option("SendApprovedReply", new Consideration((_,a) => a.Bb.GetOrDefault(K("approvalGranted"), false) && !a.Bb.GetOrDefault(K("sentMail"), false) ? 1f : 0f), "SendApprovedReply"),
                Ai.Option("DraftReply", new Consideration((_,a) => !a.Bb.GetOrDefault(K("approvalGranted"), false) && !a.Bb.GetOrDefault(K("createdDraft"), false) ? 1f : 0f), "DraftReply"),
                Ai.Option("CreateCalendarEvent", new Consideration((_,a) => a.Bb.GetOrDefault(K("approvalGranted"), false) && a.Bb.GetOrDefault(K("sentMail"), false) && !a.Bb.GetOrDefault(K("createdEvent"), false) ? 0.5f : 0f), "CreateCalendarEvent"),
                Ai.Option("Idle", Consideration.Constant(0.1f), "Idle"),
            ], hysteresis: 0f, minCommitSeconds: 0f);
            yield return Ai.Steady("decided");
        }

        static IEnumerator<AiStep> IdleNode() { yield return Ai.Steady("done"); }
    }

    private static AiCtx MakeCtx(ActuatorHost host)
    {
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "root" };
        graph.Add(new HfsmStateDef { Id = "root", Node = static _ => Empty() });
        var brain = new HfsmInstance(graph);
        var agent = new AiAgent(brain);
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host);
    }

    private static IEnumerator<AiStep> Empty() { yield break; }

    private static string DispatchText(ActuatorHost host, AiCtx ctx, SemanticKernelFunctionCommand command)
    {
        var result = host.Dispatch(ctx, command);
        if (!result.Ok) return string.Empty;
        return result.Payload is SemanticKernelFunctionResult sk ? sk.ResultText : result.Payload?.ToString() ?? string.Empty;
    }

    private sealed class FakeSemanticKernelHandler(FakeGraphPlugin fake, IReadOnlyList<AllowedSemanticKernelFunction> allowed) : IActuationHandler<SemanticKernelFunctionCommand>
    {
        private readonly HashSet<string> _allowed = allowed.Select(a => $"{a.PluginName}.{a.FunctionName}").ToHashSet(StringComparer.Ordinal);

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, SemanticKernelFunctionCommand cmd)
        {
            var name = $"{cmd.PluginName}.{cmd.FunctionName}";
            if (!_allowed.Contains(name)) return new(true, true, false, "Semantic Kernel function is not allowlisted.");
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(cmd.ArgumentsJson) ? "{}" : cmd.ArgumentsJson);
            var root = args.RootElement;
            var idArg = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            var bodyArg = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? string.Empty : string.Empty;

            string text = name switch
            {
                "graph.mail.list_messages" => fake.Mail.list_messages(),
                "graph.mail.read_message" => fake.Mail.read_message(idArg),
                "graph.mail.create_draft" => fake.Mail.create_draft(idArg, bodyArg),
                "graph.mail.send_message" => fake.Mail.send_message(idArg, bodyArg),
                "graph.calendar.list_events" => fake.Calendar.list_events(),
                "graph.calendar.create_event" => fake.Calendar.create_event(idArg),
                _ => string.Empty,
            };

            return ActuatorHost.HandlerResult.CompletedWithPayload(new SemanticKernelFunctionResult(cmd.PluginName, cmd.FunctionName, text));
        }
    }

    private sealed class FakeGraphPlugin
    {
        public List<string> Invocations { get; } = [];
        public bool SentMail { get; private set; }
        public bool CreatedEvent { get; private set; }
        public bool CreatedDraft { get; private set; }
        public string UrgentMailSubject { get; } = "Urgent: deployment status check";
        public string UrgentMailSender { get; } = "customer@example.com";
        public string? GeneratedDraftText { get; set; }
        public string? DraftBody { get; private set; }
        public string? SentBody { get; private set; }
        public MailPlugin Mail => new(this);
        public CalendarPlugin Calendar => new(this);

        public sealed class MailPlugin(FakeGraphPlugin parent)
        {
            public string list_messages() { parent.Invocations.Add("graph.mail.list_messages"); return "mail-urgent|mail-schedule|mail-newsletter"; }
            public string read_message(string id) { parent.Invocations.Add("graph.mail.read_message"); return "Can you confirm whether the deployment is still on track?"; }
            public string create_draft(string id, string body) { parent.Invocations.Add("graph.mail.create_draft"); parent.CreatedDraft = true; parent.DraftBody = body; return $"draft-created:{id}|body:{body}"; }
            public string send_message(string id, string body) { parent.Invocations.Add("graph.mail.send_message"); parent.SentMail = true; parent.SentBody = body; return $"sent:{id}|body:{body}"; }
        }

        public sealed class CalendarPlugin(FakeGraphPlugin parent)
        {
            public string list_events() { parent.Invocations.Add("graph.calendar.list_events"); return "busy:mon-10am;free:tue-2pm"; }
            public string create_event(string id) { parent.Invocations.Add("graph.calendar.create_event"); parent.CreatedEvent = true; return $"event-created:{id}"; }
        }
    }
}
