namespace Dominatus.Core.Runtime;

public sealed class AiWorld
{
    public AiClock Clock { get; } = new();

    private readonly List<AiAgent> _agents = new();

    public void Add(AiAgent agent) => _agents.Add(agent);

    public void Tick(float dt)
    {
        Clock.Advance(dt);
        for (int i = 0; i < _agents.Count; i++)
            _agents[i].Tick(this);
    }
}