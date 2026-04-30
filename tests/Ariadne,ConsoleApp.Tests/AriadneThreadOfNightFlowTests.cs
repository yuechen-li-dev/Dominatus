using Ariadne.ConsoleApp.Scripts;
using Ariadne.OptFlow.Commands;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;
using Xunit;

namespace Ariadne.ConsoleApp.Tests;

public sealed class AriadneThreadOfNightFlowTests
{
    [Fact]
    public void Chamber_KnifeChoice_ReturnsToChamber_Then_AllowsTheseusSelection()
    {
        var choosePrompts = new List<string>();

        var host = new ActuatorHost();
        host.Register(new AutoLineHandler());
        host.Register(new SequenceChooseHandler(
            onPrompt: prompt => choosePrompts.Add(prompt),
            scriptedChoices: new[] { "knife", "theseus" }));
        host.Register(new AutoAskHandler("unused"));

        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = AriadneThreadOfNight.Root });
        graph.Add(new HfsmStateDef { Id = "Intro", Node = AriadneThreadOfNight.Intro });
        graph.Add(new HfsmStateDef { Id = "Chamber", Node = AriadneThreadOfNight.Chamber });
        graph.Add(new HfsmStateDef { Id = "InspectThread", Node = AriadneThreadOfNight.InspectThread });
        graph.Add(new HfsmStateDef { Id = "InspectKnife", Node = AriadneThreadOfNight.InspectKnife });
        graph.Add(new HfsmStateDef { Id = "ReadTablets", Node = AriadneThreadOfNight.ReadTablets });
        graph.Add(new HfsmStateDef { Id = "VisitShrine", Node = AriadneThreadOfNight.VisitShrine });
        graph.Add(new HfsmStateDef { Id = "Theseus", Node = AriadneThreadOfNight.Theseus });
        graph.Add(new HfsmStateDef { Id = "TalkToTheseusWhy", Node = AriadneThreadOfNight.TalkToTheseusWhy });
        graph.Add(new HfsmStateDef { Id = "TalkToTheseusFear", Node = AriadneThreadOfNight.TalkToTheseusFear });
        graph.Add(new HfsmStateDef { Id = "TalkToTheseusMonster", Node = AriadneThreadOfNight.TalkToTheseusMonster });
        graph.Add(new HfsmStateDef { Id = "DemandPromise", Node = AriadneThreadOfNight.DemandPromise });
        graph.Add(new HfsmStateDef { Id = "Threshold", Node = AriadneThreadOfNight.Threshold });
        graph.Add(new HfsmStateDef { Id = "Ending_ThreadAndFlight", Node = AriadneThreadOfNight.Ending_ThreadAndFlight });
        graph.Add(new HfsmStateDef { Id = "Ending_MercyInTheDark", Node = AriadneThreadOfNight.Ending_MercyInTheDark });
        graph.Add(new HfsmStateDef { Id = "Ending_CrownOfKnives", Node = AriadneThreadOfNight.Ending_CrownOfKnives });
        graph.Add(new HfsmStateDef { Id = "Ending_TheDescent", Node = AriadneThreadOfNight.Ending_TheDescent });
        graph.Add(new HfsmStateDef { Id = "Ending_ThreadlessTragedy", Node = AriadneThreadOfNight.Ending_ThreadlessTragedy });

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        // Run enough ticks to:
        // - pass Intro
        // - hit Chamber choose #1 => "knife"
        // - run InspectKnife to completion
        // - hit Chamber choose #2 => "theseus"
        // - transition into Theseus
        for (int i = 0; i < 200; i++)
            world.Tick(0.01f);

        // We expect the chamber menu to have been shown twice:
        // once initially, once again after knife inspection returns via Pop().
        Assert.True(choosePrompts.Count >= 2);

        Assert.Equal(
            "Your chamber holds its breath. What do you do?",
            choosePrompts[0]);

        Assert.Equal(
            "Your chamber holds its breath. What do you do?",
            choosePrompts[1]);

        // And we should have progressed beyond the chamber into the Theseus scene.
        Assert.Contains("What do you say to Theseus?", choosePrompts);
    }

    private sealed class AutoLineHandler : IActuationHandler<DiagLineCommand>
    {
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagLineCommand cmd)
            => new(Accepted: true, Completed: true, Ok: true);
    }

    private sealed class AutoAskHandler : IActuationHandler<DiagAskCommand>
    {
        private readonly string _value;

        public AutoAskHandler(string value)
        {
            _value = value;
        }

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagAskCommand cmd)
            => ActuatorHost.HandlerResult.CompletedWithPayload(_value);
    }

    private sealed class SequenceChooseHandler : IActuationHandler<DiagChooseCommand>
    {
        private readonly Queue<string> _choices;
        private readonly Action<string>? _onPrompt;

        public SequenceChooseHandler(IEnumerable<string> scriptedChoices, Action<string>? onPrompt = null)
        {
            _choices = new Queue<string>(scriptedChoices);
            _onPrompt = onPrompt;
        }

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand cmd)
        {
            _onPrompt?.Invoke(cmd.Prompt);

            var choice = _choices.Count > 0 ? _choices.Dequeue() : cmd.Options[0].Key;

            return ActuatorHost.HandlerResult.CompletedWithPayload(choice);
        }
    }
}
