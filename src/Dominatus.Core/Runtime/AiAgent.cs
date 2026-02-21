using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;

namespace Dominatus.Core.Runtime;

public sealed class AiAgent
{
    public Blackboard.Blackboard Bb { get; } = new();
    public HfsmInstance Brain { get; }

    public AiAgent(HfsmInstance brain) => Brain = brain;

    public void Tick(AiWorld world) => Brain.Tick(world, this);
}