using System.Threading;
using Ariadne.OptFlow.Commands;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;

namespace Dominatus.StrideConn.Tests;

public sealed class StrideDialogueActuationHandlerTests
{
    [Fact]
    public void Line_CompletesAfterSurfaceAdvance()
    {
        var surface = new FakeSurface();
        var handler = new StrideDialogueActuationHandler(surface);
        var host = new ActuatorHost();

        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(new HfsmGraph { Root = "Root" }));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, host);

        var result = handler.Handle(host, ctx, new ActuationId(7), new DiagLineCommand("hello", "narrator"));

        Assert.True(result.Accepted);
        Assert.False(result.Completed);

        surface.Advance();
        host.Tick(world);

        var cursor = new EventCursor();
        Assert.True(agent.Events.TryConsume(ref cursor, (ActuationCompleted e) => e.Id.Equals(new ActuationId(7)), out _));
    }

    [Fact]
    public void Choose_CompletesWithSelectedKey()
    {
        var surface = new FakeSurface();
        var handler = new StrideDialogueActuationHandler(surface);
        var host = new ActuatorHost();

        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(new HfsmGraph { Root = "Root" }));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, host);

        var result = handler.Handle(host, ctx, new ActuationId(8), new DiagChooseCommand("pick", [new DiagChoice("a", "A")]));

        Assert.True(result.Accepted);
        surface.Choose("a");
        host.Tick(world);

        var cursor = new EventCursor();
        Assert.True(agent.Events.TryConsume(ref cursor, (ActuationCompleted<string> e) => e.Id.Equals(new ActuationId(8)), out var completed));
        Assert.Equal("a", completed.Payload);
    }

    [Fact]
    public void Ask_CompletesWithInputText()
    {
        var surface = new FakeSurface();
        var handler = new StrideDialogueActuationHandler(surface);
        var host = new ActuatorHost();

        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(new HfsmGraph { Root = "Root" }));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, host);

        var result = handler.Handle(host, ctx, new ActuationId(9), new DiagAskCommand("ask"));

        Assert.True(result.Accepted);
        surface.Answer("drop(player);");
        host.Tick(world);

        var cursor = new EventCursor();
        Assert.True(agent.Events.TryConsume(ref cursor, (ActuationCompleted<string> e) => e.Id.Equals(new ActuationId(9)), out var completed));
        Assert.Equal("drop(player);", completed.Payload);
    }

    private sealed class FakeSurface : IStrideDialogueSurface
    {
        private Action? _advance;
        private Action<string>? _choose;
        private Action<string>? _ask;

        public bool TryShowLine(DiagLineCommand command, Action onAdvance)
        {
            _advance = onAdvance;
            return true;
        }

        public bool TryShowChoose(DiagChooseCommand command, Action<string> onChoose)
        {
            _choose = onChoose;
            return true;
        }

        public bool TryShowAsk(DiagAskCommand command, Action<string> onSubmit)
        {
            _ask = onSubmit;
            return true;
        }

        public void Advance() => _advance?.Invoke();
        public void Choose(string value) => _choose?.Invoke(value);
        public void Answer(string value) => _ask?.Invoke(value);
    }
}
