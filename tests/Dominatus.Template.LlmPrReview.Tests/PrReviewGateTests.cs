using Dominatus.Template.LlmPrReview;

public sealed class PrReviewGateTests
{
    [Fact]
    public async Task PrReview_PrimitiveChoice_IsDocumentedByMetadata()
    {
        var client = new FakePrReviewLlmClient();
        var gate = new PrReviewGate(client, "fake", "scripted-v1");

        var result = await gate.ReviewWithMetadataAsync("diff --git a/src/Foo.cs b/src/Foo.cs\n+return value + 1;", 5);

        Assert.True(result.Metadata.UsedAiWorld);
        Assert.True(result.Metadata.UsedAiAgent);
        Assert.True(result.Metadata.UsedHfsm);
        Assert.True(result.Metadata.UsedLlmCall);
        Assert.False(result.Metadata.UsedLlmDecide);
        Assert.Equal(1, result.Metadata.LlmCallCount);
        Assert.Equal(0, result.Metadata.LlmDecideCount);
        Assert.Equal(PrReviewGate.PrimitiveChoice, result.Metadata.PrimitiveChoice);
        Assert.Equal(PrReviewGate.ReviewWorkflowShape, result.Metadata.ReviewWorkflowShape);
        Assert.Contains("Llm.Decide exists", result.Metadata.PrimitiveChoiceRationale);
        Assert.Contains(PrReviewGate.StableId, result.Metadata.StableIds);
    }

    [Fact]
    public async Task PrReview_KeepsLlmCallOnlyIfDecideUnsuitable()
    {
        var result = await NewGate().ReviewWithMetadataAsync("diff --git a/src/PaymentRouter.cs b/src/PaymentRouter.cs\n+++ b/src/PaymentRouter.cs\n+// possible null dereference after retry fallback", 5);

        Assert.Equal(PrReviewVerdict.Fail, result.Result.Verdict);
        Assert.True(result.Metadata.UsedLlmCall);
        Assert.False(result.Metadata.UsedLlmDecide);
        Assert.Contains("structured review report", result.Metadata.PrimitiveChoiceRationale);
        Assert.Contains("live OpenRouter text-client support", result.Metadata.PrimitiveChoiceRationale);
    }

    [Fact]
    public async Task PrReview_HfsmModelsLifecycle_NotCeremony()
    {
        var result = await NewGate().ReviewWithMetadataAsync("diff --git a/src/Foo.cs b/src/Foo.cs\n+return value;", 3);

        Assert.Contains("LoadDiff", result.Metadata.WorkflowSteps);
        Assert.Contains("Review", result.Metadata.WorkflowSteps);
        Assert.Contains("Evaluate", result.Metadata.WorkflowSteps);
        Assert.Contains("Report", result.Metadata.WorkflowSteps);
        Assert.True(result.Metadata.TypedResultStoredOnBlackboard);
    }

    [Fact]
    public void PrReview_DoesNotCallGenerateTextAsyncDirectlyFromGate()
    {
        var gateSource = File.ReadAllText(Path.Combine(RepoRoot(), "samples/Templates/Dominatus.Template.LlmPrReview/PrReviewGate.cs"));

        Assert.DoesNotContain("GenerateTextAsync", gateSource);
        Assert.Contains("Llm.Call", gateSource);
        Assert.Contains("ReviewRawResultKey", gateSource);
        Assert.Contains("EvaluateNode", gateSource);
    }

    [Fact]
    public async Task PrReview_FakePassFailNeedsHumanStillWorks()
    {
        var pass = await NewGate().ReviewAsync("diff --git a/src/Foo.cs b/src/Foo.cs\n+return value + 1;", 5);
        var fail = await NewGate().ReviewAsync("diff --git a/src/PaymentRouter.cs b/src/PaymentRouter.cs\n+++ b/src/PaymentRouter.cs\n+// possible null dereference after retry fallback", 5);
        var needsHuman = await NewGate().ReviewAsync("diff --git a/db/migration.sql b/db/migration.sql\n+-- ambiguous migration needs human approval", 5);

        Assert.Equal(PrReviewVerdict.Pass, pass.Verdict);
        Assert.Equal(PrReviewVerdict.Fail, fail.Verdict);
        Assert.Equal(PrReviewVerdict.NeedsHuman, needsHuman.Verdict);
        Assert.Equal(0, PrReviewGate.ExitCodeFor(pass, PrReviewVerdict.NeedsHuman));
        Assert.Equal(1, PrReviewGate.ExitCodeFor(fail, PrReviewVerdict.NeedsHuman));
        Assert.Equal(2, PrReviewGate.ExitCodeFor(needsHuman, PrReviewVerdict.NeedsHuman));
    }

    [Fact]
    public async Task PrReview_StyleOnlyNitSuppressed()
    {
        var result = await NewGate().ReviewAsync("diff --git a/src/Foo.cs b/src/Foo.cs\n+// rename local variable and adjust formatting only", 5);

        Assert.Equal(PrReviewVerdict.Pass, result.Verdict);
        Assert.Empty(result.Issues);
        Assert.Contains(result.NonBlockingNotes, note => note.Contains("nits", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrReview_LiveWithoutKeyRefusesBeforeProviderCall()
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
    public async Task PrReview_NoSecretsInOutput()
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
    public async Task PrReview_PromptContainsPassFailNeedsHumanAndNoNitpickInstructions()
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
    public async Task PrReview_ResultStoredOnBlackboard()
    {
        var result = await NewGate().ReviewWithMetadataAsync("diff --git a/src/Foo.cs b/src/Foo.cs\n+return value;", 3);

        Assert.True(result.Metadata.ResultStoredOnBlackboard);
        Assert.True(result.Metadata.TypedResultStoredOnBlackboard);
        Assert.False(string.IsNullOrWhiteSpace(result.RawText));
        Assert.Contains(PrReviewGate.StableId, result.ResultJson);
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

    private static PrReviewGate NewGate() => new(new FakePrReviewLlmClient(), "fake", "scripted-v1");

    private static string WriteTempDiff(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-pr-review-{Guid.NewGuid():N}.diff");
        File.WriteAllText(path, text);
        return path;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dominatus.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }
}
