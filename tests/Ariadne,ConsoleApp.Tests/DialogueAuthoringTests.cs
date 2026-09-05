using System.Collections.Immutable;
using Ariadne.ConsoleApp.Scripts;
using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Ariadne.OptFlow.Dialogue;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Ariadne.ConsoleApp.Tests;

public sealed class DialogueAuthoringTests
{
    private static readonly BbKey<bool> Enabled = new("test.dialogue.enabled");

    [Fact]
    public void Builder_constructs_immutable_plain_authoring_data()
    {
        var dialogue = Dialogue.Define<TestIntent>("test.model", builder =>
        {
            builder.Narrate("narration", DialogueText.Key("content.narration", "A quiet room."));
            builder.Say("hello", "Mara", "Morning.");
            builder.When("enabled", DialogueCondition.IsTrue(Enabled), branch =>
            {
                branch.Emit("effect", new TestIntent("enabled"));
            });
            builder.End();
        });

        Assert.Equal("test.model", dialogue.Id.Value);
        Assert.IsType<ImmutableArray<DialogueElement<TestIntent>>>(dialogue.Elements);
        var narration = Assert.IsType<DialogueLine<TestIntent>>(dialogue.Elements[0]);
        Assert.Null(narration.Speaker);
        Assert.Equal("content.narration", narration.Text.ContentId?.Value);
        Assert.DoesNotContain(
            dialogue.Elements,
            element => element.GetType().GetProperties().Any(property => property.PropertyType == typeof(AiCtx)));
    }

    [Fact]
    public void Lowering_is_deterministic_and_assigns_one_state_per_semantic_operation()
    {
        DialogueDefinition<TestIntent> dialogue = BasicDialogue();

        var first = DialogueLowerer.Lower(dialogue);
        var second = DialogueLowerer.Lower(dialogue);

        Assert.Equal(
            first.Flow.States.Select(state => state.Id.Value),
            second.Flow.States.Select(state => state.Id.Value));
        Assert.Contains(first.Inspection.States, state =>
            state.Construct == "line"
            && state.StateId.Value == "dialogue:test.basic:line:first"
            && state.OperationId == "dialogue.test.basic.line.first");
        Assert.Contains(first.Inspection.States, state => state.Construct == "choice");
        Assert.Contains(first.Inspection.States, state => state.Construct == "effect");
        Assert.True(first.Flow.Inspect().Diagnostics.Count == 0);
    }

    [Fact]
    public void Lowered_line_command_carries_composite_semantic_metadata()
    {
        var dialogue = Dialogue.Define<TestIntent>("test.metadata", builder =>
        {
            builder.Say(
                "greeting",
                "Mara",
                DialogueText.Key("content.greeting", "Morning."));
            builder.End();
        });
        var handler = new ScriptedHandler([]);
        var built = Build(dialogue, handler);

        TickUntilComplete(built.world, built.lowered, built.agent);

        DiagLineCommand command = Assert.Single(handler.LineCommands);
        Assert.Equal(new DialogueId("test.metadata"), command.DialogueId);
        Assert.Equal(new DialogueLineId("greeting"), command.LineId);
        Assert.Equal(new DialogueContentId("content.greeting"), command.ContentId);
        Assert.Equal("dialogue.test.metadata.line.greeting", command.SemanticOperationId?.Value);
    }

    [Fact]
    public void Execution_preserves_line_choice_and_effect_order()
    {
        var handler = new ScriptedHandler(["take"]);
        var built = Build(BasicDialogue(), handler);
        built.agent.Bb.Set(Enabled, true);

        TickUntilComplete(built.world, built.lowered, built.agent);

        Assert.Equal(["first", "narration"], handler.LineIds);
        Assert.Equal(["skip", "take"], handler.PresentedChoices.Single());
        Assert.Equal(["taken"], handler.Effects.Select(effect => effect.Name));
        Assert.True(built.lowered.IsComplete(built.agent));
    }

    [Fact]
    public void Choice_availability_is_explicit_and_declaration_order_is_preserved()
    {
        var handler = new ScriptedHandler(["always"]);
        var dialogue = Dialogue.Define<TestIntent>("test.availability", builder =>
        {
            builder.Choice("menu", "Choose", choices =>
            {
                choices.OptionWhen("gated", "Gated", "enabled", DialogueCondition.IsTrue(Enabled), branch => branch.End("gated-end"));
                choices.Option("always", "Always", branch => branch.End("always-end"));
            });
        });
        var built = Build(dialogue, handler);

        TickUntilComplete(built.world, built.lowered, built.agent);

        Assert.Equal(["always"], handler.PresentedChoices.Single());
    }

    [Fact]
    public void Choice_with_every_option_hidden_fails_before_dispatch()
    {
        var dialogue = Dialogue.Define<TestIntent>("test.none-available", builder =>
        {
            builder.Choice("menu", "Choose", choices =>
            {
                choices.OptionWhen(
                    "hidden",
                    "Hidden",
                    "enabled",
                    DialogueCondition.IsTrue(Enabled),
                    branch => branch.End("hidden-end"));
            });
        });
        DialogueLoweringResult<TestIntent> lowered = DialogueLowerer.Lower(dialogue);
        FlowState state = lowered.Flow.States.Single(value =>
            value.Id.Value == "dialogue:test.none-available:choice:menu");
        var host = new ActuatorHost();
        var graph = new HfsmGraph { Root = state.Id };
        graph.Add(new HfsmStateDef { Id = state.Id, Node = state.Node });
        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        IEnumerator<AiStep> enumerator = state.Node(AiCtxFactories.Live(world, agent, CancellationToken.None));

        Assert.Throws<DialogueChoiceAvailabilityException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void Call_returns_to_the_authored_continuation()
    {
        var child = Dialogue.Define<TestIntent>("test.child", builder =>
        {
            builder.Say("inside", "Child", "Inside");
            builder.End();
        });
        var parent = Dialogue.Define<TestIntent>("test.parent", builder =>
        {
            builder.Say("before", "Parent", "Before");
            builder.Call("child-call", child);
            builder.Say("after", "Parent", "After");
            builder.End();
        });
        var handler = new ScriptedHandler([]);
        var built = Build(parent, handler);

        TickUntilComplete(built.world, built.lowered, built.agent);

        Assert.Equal(["before", "inside", "after"], handler.LineIds);
        Assert.Contains(built.lowered.Inspection.States, state =>
            state.Construct == "call"
            && state.CalledDialogue == child.Id);
    }

    [Fact]
    public void Explicit_menu_loop_reuses_choice_without_stale_pending_state()
    {
        var handler = new ScriptedHandler(["topic", "back"]);
        var dialogue = Dialogue.Define<TestIntent>("test.loop", builder =>
        {
            builder.Choice("topics", "Topics", choices =>
            {
                choices.Option("topic", "Ask", branch =>
                {
                    branch.Say("answer", "Mara", "An answer.");
                    branch.LoopTo("again", "topics");
                });
                choices.Option("back", "Back", branch => branch.End("back-end"));
            });
        });
        var built = Build(dialogue, handler);

        TickUntilComplete(built.world, built.lowered, built.agent);

        Assert.Equal(2, handler.PresentedChoices.Count);
        Assert.Equal(["answer"], handler.LineIds);
        Assert.True(built.lowered.IsComplete(built.agent));
    }

    [Fact]
    public void Pending_choice_checkpoint_restores_same_program_position_without_replay()
    {
        var definition = Dialogue.Define<TestIntent>("test.pending", builder =>
        {
            builder.Say("line-a", "Mara", "A");
            builder.Say("line-b", "Mara", "B");
            builder.Choice("choice", "Now?", choices =>
            {
                choices.Option("yes", "Yes", branch => branch.End("yes-end"));
            });
        });
        var firstHandler = new DeferredChoiceHandler();
        var first = Build(definition, firstHandler);
        TickUntil(() => firstHandler.ChoiceDispatches == 1, first.world);

        Assert.Equal(["line-a", "line-b"], firstHandler.LineIds);
        Assert.Equal(1, firstHandler.ChoiceDispatches);
        var beforePath = first.agent.Brain.GetActivePath().Select(state => state.Value).ToArray();
        var checkpoint = DominatusCheckpointBuilder.Capture(first.world);

        var restoredHandler = new DeferredChoiceHandler();
        var restored = Build(definition, restoredHandler);
        var cursors = DominatusCheckpointBuilder.Restore(restored.world, checkpoint);
        restored.world.Tick(.016f);

        Assert.Empty(restoredHandler.LineIds);
        Assert.Equal(0, restoredHandler.ChoiceDispatches);
        Assert.Equal(beforePath, restored.agent.Brain.GetActivePath().Select(state => state.Value));
        Assert.Equal(
            checkpoint.Agents[0].EventCursorBlob,
            DominatusCheckpointBuilder.Capture(restored.world).Agents[0].EventCursorBlob);

        new ReplayDriver(
            restored.world,
            new ReplayLog(1, [new ReplayEvent.Choice(restored.agent.Id.ToString(), "yes")]),
            cursors).ApplyAll();
        TickUntilComplete(restored.world, restored.lowered, restored.agent);
        Assert.True(restored.lowered.IsComplete(restored.agent));
    }

    [Fact]
    public void Completed_effect_is_not_replayed_after_restore()
    {
        var definition = Dialogue.Define<TestIntent>("test.effect-restore", builder =>
        {
            builder.Emit("once", new TestIntent("once"));
            builder.Choice("hold", "Hold", choices =>
            {
                choices.Option("done", "Done", branch => branch.End("done-end"));
            });
        });
        var firstHandler = new DeferredChoiceHandler();
        var first = Build(definition, firstHandler);
        TickUntil(() => firstHandler.ChoiceDispatches == 1, first.world);
        Assert.Equal(["once"], firstHandler.Effects.Select(effect => effect.Name));
        var checkpoint = DominatusCheckpointBuilder.Capture(first.world);

        var restoredHandler = new DeferredChoiceHandler();
        var restored = Build(definition, restoredHandler);
        DominatusCheckpointBuilder.Restore(restored.world, checkpoint);
        restored.world.Tick(.016f);

        Assert.Empty(restoredHandler.Effects);
        Assert.Equal(0, restoredHandler.ChoiceDispatches);
    }

    [Fact]
    public void Nested_call_checkpoint_reenters_child_without_duplicate_push_or_line()
    {
        var child = Dialogue.Define<TestIntent>("test.save-child", builder =>
        {
            builder.Say("inside", "Child", "Inside");
            builder.End();
        });
        var parent = Dialogue.Define<TestIntent>("test.save-parent", builder =>
        {
            builder.Call("child", child);
            builder.Say("after", "Parent", "After");
            builder.End();
        });
        var firstHandler = new DeferredLineHandler();
        var first = Build(parent, firstHandler);
        TickUntil(() => firstHandler.LineIds.Count == 1, first.world);
        Assert.Equal(["inside"], firstHandler.LineIds);
        var checkpoint = DominatusCheckpointBuilder.Capture(first.world);

        var restoredHandler = new DeferredLineHandler();
        var restored = Build(parent, restoredHandler);
        var cursors = DominatusCheckpointBuilder.Restore(restored.world, checkpoint);
        restored.world.Tick(.016f);
        Assert.Empty(restoredHandler.LineIds);

        new ReplayDriver(
            restored.world,
            new ReplayLog(1, [new ReplayEvent.Advance(restored.agent.Id.ToString())]),
            cursors).ApplyAll();
        TickUntil(() => restoredHandler.LineIds.Count == 1, restored.world);

        Assert.Equal(["after"], restoredHandler.LineIds);
        Assert.Contains("dialogue:test.save-parent:line:after", restored.agent.Brain.GetActivePath().Select(state => state.Value));
    }

    [Fact]
    public void Invalid_choice_payloads_and_failed_completions_are_typed_diagnostics()
    {
        Assert.IsType<DiagDispatchException>(ConsumeRejectedChoice());

        var missing = ConsumeChoice(new ActuationCompleted<string>(new ActuationId(1), true, null, null));
        Assert.IsType<DiagPayloadException>(missing);

        var failed = ConsumeChoice(new ActuationCompleted<string>(new ActuationId(1), false, "failed", null));
        Assert.IsType<DiagCompletionException>(failed);

        var definition = Dialogue.Define<TestIntent>("test.invalid-choice", builder =>
        {
            builder.Choice("choice", "Choose", choices =>
            {
                choices.Option("known", "Known", branch => branch.End("known-end"));
            });
        });
        var selected = Assert.Throws<DialogueChoiceSelectionException>(() =>
            RunChoiceNodeDirectly(definition, "unknown"));
        Assert.Equal("unknown", selected.SelectedId);
        Assert.Equal([new DialogueChoiceOptionId("known")], selected.PresentedOptions);
    }

    [Fact]
    public void Rust_simulator_raw_and_high_level_selected_trace_have_exact_parity()
    {
        var rawHandler = new RustParityHandler(["velvet", "back"], applyHighLevelEffects: false);
        var raw = BuildRawRustAiHelp(rawHandler);
        raw.agent.Bb.Set(RustSimulator.Confidence, 2);
        TickUntil(() => raw.agent.Bb.GetOrDefault(RawComplete, false), raw.world);

        var highHandler = new RustParityHandler(["velvet", "back"], applyHighLevelEffects: true);
        var high = BuildRust(RustSimulatorHighLevelDialogue.AiHelp, highHandler);
        high.agent.Bb.Set(RustSimulator.Confidence, 2);
        TickUntil(() => high.lowered.IsComplete(high.agent), high.world);

        Assert.Equal(
            rawHandler.Lines.Select(line => (line.Speaker == "Narrator" ? null : line.Speaker, line.Text)),
            highHandler.Lines);
        Assert.Equal(rawHandler.PresentedChoices, highHandler.PresentedChoices);
        Assert.Equal(raw.agent.Bb.GetOrDefault(RustSimulator.AskedVelvet, false), high.agent.Bb.GetOrDefault(RustSimulator.AskedVelvet, false));
        Assert.Equal(raw.agent.Bb.GetOrDefault(RustSimulator.Confidence, 0), high.agent.Bb.GetOrDefault(RustSimulator.Confidence, 0));
        Assert.Equal(7, highHandler.Lines.Count);
        Assert.Equal(2, highHandler.Effects.Count);
        Assert.Equal(50, high.lowered.Flow.States.Count);
        Assert.Equal(33, high.lowered.Inspection.States.Count(state => state.OperationId is not null));
    }

    [Fact]
    public void Validator_reports_structural_authoring_hazards_before_lowering()
    {
        var duplicate = Dialogue.Define<TestIntent>("test.duplicates", builder =>
        {
            builder.Say("same", "Mara", "One");
            builder.Say("same", "Mara", "Two");
            builder.End();
        });
        AssertCode(duplicate, DialogueValidationCode.DuplicateLineId);

        var emptyChoice = Dialogue.Define<TestIntent>("test.empty-choice", builder =>
        {
            builder.Choice("empty", "Empty", _ => { });
            builder.End();
        });
        AssertCode(emptyChoice, DialogueValidationCode.ChoiceHasNoOptions);

        var missingTarget = new DialogueDefinition<TestIntent>(
            "test.missing-call",
            [new DialogueCall<TestIntent>("missing", null), new DialogueEnd<TestIntent>("end")]);
        AssertCode(missingTarget, DialogueValidationCode.MissingCallTarget);

        var invalidLoop = Dialogue.Define<TestIntent>("test.invalid-loop", builder =>
        {
            builder.LoopTo("loop", "missing");
        });
        AssertCode(invalidLoop, DialogueValidationCode.InvalidLoopTarget);

        var accidentalLoopElement = (DialogueLoop<TestIntent>)Activator.CreateInstance(
            typeof(DialogueLoop<TestIntent>),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [new DialogueLoopId("loop"), "start", false],
            culture: null)!;
        var accidentalLoop = new DialogueDefinition<TestIntent>(
            "test.accidental-loop",
            [
                new DialogueLine<TestIntent>("start", null, "Start"),
                accidentalLoopElement
            ]);
        AssertCode(accidentalLoop, DialogueValidationCode.AccidentalCycle);
    }

    [Fact]
    public void Validator_checks_registries_condition_types_and_composite_identity()
    {
        var dialogue = Dialogue.Define<TestIntent>("test.registries", builder =>
        {
            builder.Say("shared", "Unknown", "Line");
            builder.When("shared", DialogueCondition.IsTrue(Enabled), _ => { });
            builder.End();
        });
        var options = new DialogueValidationOptions(
            new HashSet<DialogueSpeakerId> { new("Mara") },
            new HashSet<string>(StringComparer.Ordinal) { "some.other.key" });
        var report = DialogueValidator.Validate(dialogue, options);

        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == DialogueValidationCode.DuplicateConditionId);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == DialogueValidationCode.UnknownSpeaker);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == DialogueValidationCode.UnknownConditionKey);

        var unsupported = Dialogue.Define<TestIntent>("test.unsupported-condition", builder =>
        {
            builder.When(
                "date",
                DialogueCondition.Equals(new BbKey<DateTime>("story.date"), DateTime.UnixEpoch),
                _ => { });
            builder.End();
        });
        AssertCode(unsupported, DialogueValidationCode.UnsupportedConditionType);
    }

    [Fact]
    public void Validator_reports_each_typed_duplicate_identity_category()
    {
        var duplicateChoices = Dialogue.Define<TestIntent>("test.duplicate-choices", builder =>
        {
            builder.Choice("menu", "One", choices => choices.Option("one", "One", _ => { }));
            builder.Choice("menu", "Two", choices => choices.Option("two", "Two", _ => { }));
            builder.End();
        });
        AssertCode(duplicateChoices, DialogueValidationCode.DuplicateChoiceId);

        var duplicateOptions = Dialogue.Define<TestIntent>("test.duplicate-options", builder =>
        {
            builder.Choice("menu", "Menu", choices =>
            {
                choices.Option("same", "One", _ => { });
                choices.Option("same", "Two", _ => { });
            });
            builder.End();
        });
        AssertCode(duplicateOptions, DialogueValidationCode.DuplicateChoiceOptionId);

        var duplicateEffects = Dialogue.Define<TestIntent>("test.duplicate-effects", builder =>
        {
            builder.Emit("same", new TestIntent("one"));
            builder.Emit("same", new TestIntent("two"));
            builder.End();
        });
        AssertCode(duplicateEffects, DialogueValidationCode.DuplicateEffectId);

        var duplicateCalls = Dialogue.Define<TestIntent>("test.call-target", builder => builder.End());
        var calls = Dialogue.Define<TestIntent>("test.duplicate-calls", builder =>
        {
            builder.Call("same", duplicateCalls);
            builder.Call("same", duplicateCalls);
            builder.End();
        });
        AssertCode(calls, DialogueValidationCode.DuplicateCallId);
    }

    [Fact]
    public void Validator_rejects_distinct_call_targets_with_the_same_dialogue_id()
    {
        var first = Dialogue.Define<TestIntent>("test.shared-id", builder => builder.End("first-end"));
        var second = Dialogue.Define<TestIntent>("test.shared-id", builder => builder.End("second-end"));
        var root = Dialogue.Define<TestIntent>("test.duplicate-dialogues", builder =>
        {
            builder.Call("first", first);
            builder.Call("second", second);
            builder.End();
        });

        AssertCode(root, DialogueValidationCode.DuplicateDialogueId);
    }

    private static DialogueDefinition<TestIntent> BasicDialogue()
    {
        return Dialogue.Define<TestIntent>("test.basic", builder =>
        {
            builder.Say("first", "Mara", "First");
            builder.Narrate("narration", "Narration");
            builder.Choice("choice", "Choose", choices =>
            {
                choices.Option("skip", "Skip", _ => { });
                choices.Option("take", "Take", branch =>
                {
                    branch.When("enabled", DialogueCondition.IsTrue(Enabled), enabled =>
                    {
                        enabled.Emit("taken", new TestIntent("taken"));
                    });
                });
            });
            builder.End();
        });
    }

    private static (AiWorld world, AiAgent agent, DialogueLoweringResult<TestIntent> lowered) Build(
        DialogueDefinition<TestIntent> definition,
        TestHandler handler)
    {
        var lowered = DialogueLowerer.Lower(definition);
        var host = new ActuatorHost();
        host.Register<DiagLineCommand>(handler);
        host.Register<DiagChooseCommand>(handler);
        host.Register<DialogueEffectCommand<TestIntent>>(handler);
        var world = new AiWorld(host);
        var agent = new AiAgent(lowered.Flow.CreateBrain());
        world.Add(agent);
        return (world, agent, lowered);
    }

    private static (
        AiWorld world,
        AiAgent agent,
        DialogueLoweringResult<RustSimulatorHighLevelDialogue.GameIntent> lowered) BuildRust(
        DialogueDefinition<RustSimulatorHighLevelDialogue.GameIntent> definition,
        RustParityHandler handler)
    {
        var lowered = DialogueLowerer.Lower(definition);
        var host = new ActuatorHost();
        host.Register<DiagLineCommand>(handler);
        host.Register<DiagChooseCommand>(handler);
        host.Register<DialogueEffectCommand<RustSimulatorHighLevelDialogue.GameIntent>>(handler);
        var world = new AiWorld(host);
        var agent = new AiAgent(lowered.Flow.CreateBrain());
        world.Add(agent);
        return (world, agent, lowered);
    }

    private static void TickUntilComplete(
        AiWorld world,
        DialogueLoweringResult<TestIntent> lowered,
        AiAgent agent)
    {
        TickUntil(() => lowered.IsComplete(agent), world);
    }

    private static void TickUntil(Func<bool> complete, AiWorld world)
    {
        for (var tick = 0; tick < 50 && !complete(); tick++)
            world.Tick(.016f);
        Assert.True(complete());
    }

    private static Exception ConsumeChoice(ActuationCompleted<string> completion)
    {
        var handler = new DeferredChoiceHandler();
        var host = new ActuatorHost();
        host.Register<DiagChooseCommand>(handler);
        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = _ => Idle() });
        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        var context = AiCtxFactories.Live(world, agent, CancellationToken.None);
        var step = new DiagSteps.ChooseStep("Choose", [new DiagChoice("known", "Known")], new BbKey<string>("result"), "test.direct");
        var cursor = default(EventCursor);
        Assert.False(step.TryConsume(context, ref cursor));
        agent.Events.Publish(completion);
        return Record.Exception(() => step.TryConsume(context, ref cursor))!;
    }

    private static Exception ConsumeRejectedChoice()
    {
        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = _ => Idle() });
        var world = new AiWorld(new ActuatorHost());
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        var context = AiCtxFactories.Live(world, agent, CancellationToken.None);
        var step = new DiagSteps.ChooseStep(
            "Choose",
            [new DiagChoice("known", "Known")],
            new BbKey<string>("result"),
            "test.rejected");
        var cursor = default(EventCursor);
        return Record.Exception(() => step.TryConsume(context, ref cursor))!;
    }

    private static void RunChoiceNodeDirectly(DialogueDefinition<TestIntent> definition, string choice)
    {
        var lowered = DialogueLowerer.Lower(definition);
        FlowState state = lowered.Flow.States.Single(value => value.Id.Value == "dialogue:test.invalid-choice:choice:choice");
        var host = new ActuatorHost();
        var handler = new ScriptedHandler([choice]);
        host.Register<DiagChooseCommand>(handler);
        var graph = new HfsmGraph { Root = state.Id };
        graph.Add(new HfsmStateDef { Id = state.Id, Node = state.Node });
        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        var enumerator = state.Node(AiCtxFactories.Live(world, agent, CancellationToken.None));
        Assert.True(enumerator.MoveNext());
        var wait = Assert.IsAssignableFrom<IWaitEvent>(enumerator.Current);
        var cursor = wait.CreateInitialCursor(AiCtxFactories.Live(world, agent, CancellationToken.None));
        Assert.True(wait.TryConsume(AiCtxFactories.Live(world, agent, CancellationToken.None), ref cursor));
        enumerator.MoveNext();
    }

    private static readonly BbKey<bool> RawComplete = new("test.rust.raw-complete");

    private static (AiWorld world, AiAgent agent) BuildRawRustAiHelp(RustParityHandler handler)
    {
        static IEnumerator<AiStep> Root(AiCtx _)
        {
            yield return Ai.Push("Level1_AIHelp");
            yield return Ai.MatchReturn(
                Ai.OnSuccess("done"),
                Ai.OnReturn("done"),
                Ai.OnFailure("done"));
        }

        static IEnumerator<AiStep> Done(AiCtx context)
        {
            context.Bb.Set(RawComplete, true);
            yield return Ai.Succeed();
        }

        var states = new[]
        {
            Flow.State("root", Root),
            Flow.State("Level1_AIHelp", RustSimulator.Level1_AIHelp),
            Flow.State("Level1_AskVelvet", RustSimulator.Level1_AskVelvet),
            Flow.State("Level1_AskNimbus", RustSimulator.Level1_AskNimbus),
            Flow.State("Level1_AskMiniJim", RustSimulator.Level1_AskMiniJim),
            Flow.State("done", Done)
        };
        var flow = Flow.Define("test.rust.raw", states[0], states);
        var host = new ActuatorHost();
        host.Register<DiagLineCommand>(handler);
        host.Register<DiagChooseCommand>(handler);
        var world = new AiWorld(host);
        var agent = new AiAgent(flow.CreateBrain());
        world.Add(agent);
        return (world, agent);
    }

    private static IEnumerator<AiStep> Idle()
    {
        while (true)
            yield return Ai.Steady();
    }

    private static void AssertCode(DialogueDefinition<TestIntent> definition, DialogueValidationCode code)
    {
        DialogueValidationReport report = DialogueValidator.Validate(definition);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Throws<DialogueValidationException>(() => DialogueLowerer.Lower(definition));
    }

    private sealed record TestIntent(string Name);

    private abstract class TestHandler :
        IActuationHandler<DiagLineCommand>,
        IActuationHandler<DiagChooseCommand>,
        IActuationHandler<DialogueEffectCommand<TestIntent>>
    {
        public List<string> LineIds { get; } = [];
        public List<DiagLineCommand> LineCommands { get; } = [];
        public List<TestIntent> Effects { get; } = [];
        public List<string[]> PresentedChoices { get; } = [];

        public virtual ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagLineCommand command)
        {
            LineCommands.Add(command);
            LineIds.Add(command.LineId?.Value ?? command.SemanticOperationId?.Value ?? command.Text);
            return ActuatorHost.HandlerResult.CompletedOk();
        }

        public abstract ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand command);

        public virtual ActuatorHost.HandlerResult Handle(
            ActuatorHost host,
            AiCtx ctx,
            ActuationId id,
            DialogueEffectCommand<TestIntent> command)
        {
            Effects.Add(command.Consequence);
            return ActuatorHost.HandlerResult.CompletedOk();
        }
    }

    private sealed class ScriptedHandler(IEnumerable<string> choices) : TestHandler
    {
        private readonly Queue<string> _choices = new(choices);

        public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand command)
        {
            PresentedChoices.Add(command.Options.Select(option => option.Key).ToArray());
            return ActuatorHost.HandlerResult.CompletedWithPayload(_choices.Dequeue());
        }
    }

    private sealed class DeferredChoiceHandler : TestHandler
    {
        public int ChoiceDispatches { get; private set; }

        public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand command)
        {
            ChoiceDispatches++;
            PresentedChoices.Add(command.Options.Select(option => option.Key).ToArray());
            return new ActuatorHost.HandlerResult(true, false, false, PayloadType: typeof(string));
        }
    }

    private sealed class DeferredLineHandler : TestHandler
    {
        public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagLineCommand command)
        {
            LineCommands.Add(command);
            LineIds.Add(command.LineId?.Value ?? command.Text);
            return new ActuatorHost.HandlerResult(true, false, false);
        }

        public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand command)
        {
            throw new InvalidOperationException("No choice expected.");
        }
    }

    private sealed class RustParityHandler(
        IEnumerable<string> choices,
        bool applyHighLevelEffects) :
        IActuationHandler<DiagLineCommand>,
        IActuationHandler<DiagChooseCommand>,
        IActuationHandler<DialogueEffectCommand<RustSimulatorHighLevelDialogue.GameIntent>>
    {
        private readonly Queue<string> _choices = new(choices);
        private readonly bool _applyHighLevelEffects = applyHighLevelEffects;

        public List<(string? Speaker, string Text)> Lines { get; } = [];
        public List<string[]> PresentedChoices { get; } = [];
        public List<RustSimulatorHighLevelDialogue.GameIntent> Effects { get; } = [];

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagLineCommand command)
        {
            Lines.Add((command.Speaker, command.Text));
            return ActuatorHost.HandlerResult.CompletedOk();
        }

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand command)
        {
            PresentedChoices.Add(command.Options.Select(option => option.Key).ToArray());
            return ActuatorHost.HandlerResult.CompletedWithPayload(_choices.Dequeue());
        }

        public ActuatorHost.HandlerResult Handle(
            ActuatorHost host,
            AiCtx ctx,
            ActuationId id,
            DialogueEffectCommand<RustSimulatorHighLevelDialogue.GameIntent> command)
        {
            Effects.Add(command.Consequence);
            if (_applyHighLevelEffects)
            {
                switch (command.Consequence.Kind)
                {
                    case RustSimulatorHighLevelDialogue.EffectKind.MarkVelvetAsked:
                        ctx.Bb.Set(RustSimulator.AskedVelvet, true);
                        break;
                    case RustSimulatorHighLevelDialogue.EffectKind.IncreaseConfidence:
                        ctx.Bb.Set(
                            RustSimulator.Confidence,
                            ctx.Bb.GetOrDefault(RustSimulator.Confidence, 0) + command.Consequence.Amount);
                        break;
                    case RustSimulatorHighLevelDialogue.EffectKind.MarkNimbusAsked:
                        ctx.Bb.Set(RustSimulator.AskedNimbus, true);
                        break;
                    case RustSimulatorHighLevelDialogue.EffectKind.IncreaseSanity:
                        ctx.Bb.Set(
                            RustSimulator.Sanity,
                            ctx.Bb.GetOrDefault(RustSimulator.Sanity, 0) + command.Consequence.Amount);
                        break;
                    case RustSimulatorHighLevelDialogue.EffectKind.MarkMiniJimAsked:
                        ctx.Bb.Set(RustSimulator.AskedMiniJim, true);
                        break;
                    case RustSimulatorHighLevelDialogue.EffectKind.IncreaseTechDebt:
                        ctx.Bb.Set(
                            RustSimulator.TechDebt,
                            ctx.Bb.GetOrDefault(RustSimulator.TechDebt, 0) + command.Consequence.Amount);
                        break;
                }
            }
            return ActuatorHost.HandlerResult.CompletedOk();
        }
    }
}
