namespace Dominatus.Core.Runtime;

public sealed class AiClock
{
    public float DeltaTime { get; private set; }
    public float Time { get; private set; }

    public void Advance(float dt)
    {
        if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt), "dt must be non-negative");
        DeltaTime = dt;
        Time += dt;
    }
}