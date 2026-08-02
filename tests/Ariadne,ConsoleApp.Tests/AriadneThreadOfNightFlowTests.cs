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

        var brain = AriadneThreadOfNight.Definition.CreateBrain();
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
