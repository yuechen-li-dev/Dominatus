namespace Dominatus.Template.LlmPrReview;

public sealed record PrReviewCliOptions(
    string? DiffPath,
    bool ReadStdin,
    bool Fake,
    bool Live,
    string Provider,
    string? Model,
    int MaxIssues,
    PrReviewVerdict FailOn)
{
    public static PrReviewCliOptions Default { get; } = new(
        DiffPath: null,
        ReadStdin: false,
        Fake: true,
        Live: false,
        Provider: "OpenRouter",
        Model: null,
        MaxIssues: 5,
        FailOn: PrReviewVerdict.NeedsHuman);
}

public static class PrReviewCliParser
{
    public static PrReviewCliOptions Parse(IReadOnlyList<string> args)
    {
        var options = PrReviewCliOptions.Default;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--diff":
                    options = options with { DiffPath = RequireValue(args, ref i, arg), ReadStdin = false };
                    break;
                case "--stdin":
                    options = options with { ReadStdin = true, DiffPath = null };
                    break;
                case "--fake":
                    options = options with { Fake = true, Live = false };
                    break;
                case "--live":
                    options = options with { Fake = false, Live = true };
                    break;
                case "--provider":
                    options = options with { Provider = RequireValue(args, ref i, arg) };
                    break;
                case "--model":
                    options = options with { Model = RequireValue(args, ref i, arg) };
                    break;
                case "--max-issues":
                    options = options with { MaxIssues = ParsePositiveInt(RequireValue(args, ref i, arg), arg) };
                    break;
                case "--fail-on":
                    options = options with { FailOn = ParseFailOn(RequireValue(args, ref i, arg)) };
                    break;
                case "--help":
                case "-h":
                    throw new PrReviewHelpRequestedException();
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        if (!options.ReadStdin && string.IsNullOrWhiteSpace(options.DiffPath))
        {
            throw new ArgumentException("Provide --diff PATH or --stdin.");
        }

        if (options.Fake == options.Live)
        {
            throw new ArgumentException("Choose exactly one of --fake or --live.");
        }

        if (!string.Equals(options.Provider, "OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only --provider OpenRouter is supported by this starter template.");
        }

        return options;
    }

    public static string Usage => """
Dominatus PR Review Gate

Usage:
  dotnet run --project samples/Templates/Dominatus.Template.LlmPrReview -- --diff examples/sample.diff --fake
  dotnet run --project samples/Templates/Dominatus.Template.LlmPrReview -- --diff my-pr.diff --live

Options:
  --diff PATH              Read a PR diff from a local file.
  --stdin                  Read a PR diff from standard input.
  --fake                   Use deterministic fake LLM mode. Default for tests and examples.
  --live                   Use OpenRouter. Requires OPENROUTER_API_KEY.
  --model MODEL            Override DOMINATUS_PR_REVIEW_MODEL.
  --provider OpenRouter    Live provider. OpenRouter is the starter provider.
  --max-issues N           Maximum blocking/human-review issues to request. Default: 5.
  --fail-on VALUE          NeedsHuman or FailOnly. Default: NeedsHuman.
""";

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int ParsePositiveInt(string value, string option)
        => int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{option} must be a positive integer.");

    private static PrReviewVerdict ParseFailOn(string value)
        => value.Equals("NeedsHuman", StringComparison.OrdinalIgnoreCase)
            ? PrReviewVerdict.NeedsHuman
            : value.Equals("FailOnly", StringComparison.OrdinalIgnoreCase)
                ? PrReviewVerdict.Fail
                : throw new ArgumentException("--fail-on must be NeedsHuman or FailOnly.");
}

public sealed class PrReviewHelpRequestedException : Exception;
