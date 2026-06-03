using Dominatus.Llm.OptFlow;

namespace Dominatus.Template.LlmPrReview;

public static class Program
{
    public static async Task<int> Main(string[] args)
        => await PrReviewCli.RunAsync(args, Console.In, Console.Out, Console.Error).ConfigureAwait(false);
}

public static class PrReviewCli
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextReader input, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = PrReviewCliParser.Parse(args);
            var diff = options.ReadStdin ? await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false) : await File.ReadAllTextAsync(options.DiffPath!, cancellationToken).ConfigureAwait(false);
            var model = options.Model ?? Environment.GetEnvironmentVariable("DOMINATUS_PR_REVIEW_MODEL") ?? "openai/gpt-4o-mini";
            var client = CreateClient(options, model);
            var gate = new PrReviewGate(client, options.Fake ? "fake" : "openrouter", model);
            var result = await gate.ReviewAsync(diff, options.MaxIssues, cancellationToken).ConfigureAwait(false);

            output.Write(PrReviewReportWriter.Write(result));
            return PrReviewGate.ExitCodeFor(result, options.FailOn);
        }
        catch (PrReviewHelpRequestedException)
        {
            output.WriteLine(PrReviewCliParser.Usage);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or InvalidOperationException)
        {
            error.WriteLine(Sanitize(ex.Message));
            return 2;
        }
    }

    private static ILlmClient CreateClient(PrReviewCliOptions options, string model)
    {
        if (options.Fake)
        {
            return new FakePrReviewLlmClient();
        }

        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Live mode requires OPENROUTER_API_KEY. Use --fake for deterministic local runs.");
        }

        return new OpenRouterLlmClient(new HttpClient(), new OpenRouterLlmClientOptions
        {
            ApiKey = apiKey,
            Model = model,
            HttpReferer = Environment.GetEnvironmentVariable("OPENROUTER_HTTP_REFERER"),
            Title = Environment.GetEnvironmentVariable("OPENROUTER_TITLE")
        });
    }

    private static string Sanitize(string message)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        return string.IsNullOrWhiteSpace(apiKey) ? message : message.Replace(apiKey, "[redacted]", StringComparison.Ordinal);
    }
}
