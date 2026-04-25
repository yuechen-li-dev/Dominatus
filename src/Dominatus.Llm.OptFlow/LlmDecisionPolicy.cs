namespace Dominatus.Llm.OptFlow;

public sealed record LlmDecisionPolicy
{
    public static LlmDecisionPolicy Default { get; } = new(
        minCommitTicks: 60,
        rescoreEveryTicks: 60,
        hysteresisMargin: 0.15);

    public LlmDecisionPolicy(int minCommitTicks, int rescoreEveryTicks, double hysteresisMargin)
    {
        if (minCommitTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minCommitTicks), "MinCommitTicks must be greater than zero.");
        }

        if (rescoreEveryTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rescoreEveryTicks), "RescoreEveryTicks must be greater than zero.");
        }

        if (hysteresisMargin < 0.0 || hysteresisMargin > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(hysteresisMargin), "HysteresisMargin must be between 0.0 and 1.0.");
        }

        MinCommitTicks = minCommitTicks;
        RescoreEveryTicks = rescoreEveryTicks;
        HysteresisMargin = hysteresisMargin;
    }

    public int MinCommitTicks { get; }

    public int RescoreEveryTicks { get; }

    public double HysteresisMargin { get; }
}
