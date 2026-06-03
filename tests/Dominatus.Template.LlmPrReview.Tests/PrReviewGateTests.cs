using Dominatus.Template.LlmPrReview;

public sealed class PrReviewGateTests
{
    [Fact]
    public async Task FakeReviewPassPathReturnsPass()
    {
        var client = new FakePrReviewLlmClient();
        var gate = new PrReviewGate(client, "fake", "scripted-v1");

        var result = await gate.ReviewAsync("diff --git a/src/Foo.cs b/src/Foo.cs\n+return value + 1;", 5);

        Assert.Equal(PrReviewVerdict.Pass, result.Verdict);
        Assert.Equal(0, PrReviewGate.ExitCodeFor(result, PrReviewVerdict.NeedsHuman));
    }

    [Fact]
    public async Task FakeReviewFailPathReturnsFail()
    {
        var client = new FakePrReviewLlmClient();
        var gate = new PrReviewGate(client, "fake", "scripted-v1");

        var result = await gate.ReviewAsync("diff --git a/src/PaymentRouter.cs b/src/PaymentRouter.cs\n+++ b/src/PaymentRouter.cs\n+// possible null dereference after retry fallback", 5);

        Assert.Equal(PrReviewVerdict.Fail, result.Verdict);
        Assert.Contains(result.Issues, issue => issue.Severity == PrReviewIssueSeverity.Blocking);
        Assert.Equal(1, PrReviewGate.ExitCodeFor(result, PrReviewVerdict.NeedsHuman));
    }

    [Fact]
    public async Task FakeReviewSuppressesStyleOnlyNitBehavior()
    {
        var client = new FakePrReviewLlmClient();
        var gate = new PrReviewGate(client, "fake", "scripted-v1");

        var result = await gate.ReviewAsync("diff --git a/src/Foo.cs b/src/Foo.cs\n+// rename local variable and adjust formatting only", 5);

        Assert.Equal(PrReviewVerdict.Pass, result.Verdict);
        Assert.Empty(result.Issues);
        Assert.Contains(result.NonBlockingNotes, note => note.Contains("nits", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LiveModeRefusesWithoutOpenRouterApiKey()
    {
        var previous = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await PrReviewCli.RunAsync(["--diff", WriteTempDiff("+safe change"), "--live"], TextReader.Null, output, error);

            Assert.Equal(2, code);
            Assert.Contains("OPENROUTER_API_KEY", error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", previous);
        }
    }

    [Fact]
    public async Task OutputDoesNotLeakOpenRouterSecret()
    {
        const string secret = "sk-test-dominatus-secret";
        var previous = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", secret);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await PrReviewCli.RunAsync(["--diff", "/path/that/does/not/exist.diff", "--live"], TextReader.Null, output, error);

            Assert.Equal(2, code);
            Assert.DoesNotContain(secret, output.ToString());
            Assert.DoesNotContain(secret, error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", previous);
        }
    }

    [Fact]
    public async Task PromptIncludesGateVerdictsAndNitpickSuppression()
    {
        var client = new FakePrReviewLlmClient();
        var gate = new PrReviewGate(client, "fake", "scripted-v1");

        _ = await gate.ReviewAsync("diff --git a/src/Foo.cs b/src/Foo.cs\n+return value;", 3);

        Assert.NotNull(client.LastRequest);
        Assert.Contains("PASS", client.LastRequest!.Intent);
        Assert.Contains("FAIL", client.LastRequest.Intent);
        Assert.Contains("NEEDS_HUMAN", client.LastRequest.Intent);
        Assert.Contains("Do not nitpick", client.LastRequest.Intent);
    }

    [Fact]
    public async Task StdinFakePathUsesNoNetworkClient()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await PrReviewCli.RunAsync(["--stdin", "--fake"], new StringReader("+safe"), output, error);

        Assert.Equal(0, code);
        Assert.Contains("Verdict: PASS", output.ToString());
        Assert.Empty(error.ToString());
    }

    private static string WriteTempDiff(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-pr-review-{Guid.NewGuid():N}.diff");
        File.WriteAllText(path, text);
        return path;
    }
}
