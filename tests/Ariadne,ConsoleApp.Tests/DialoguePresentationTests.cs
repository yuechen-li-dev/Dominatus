using Ariadne.OptFlow;
using Ariadne.OptFlow.Presentation;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Xunit;

namespace Ariadne.ConsoleApp.Tests;

public sealed class DialoguePresentationTests
{
    [Fact]
    public void Project_ReconstructsPendingChoiceWithStableDeclarationOrder()
    {
        DialoguePresentationOperation operation = new(
            "mara.offer",
            DialoguePresentationOperationKind.Choice,
            "Mara",
            "What do you say?",
            [Diag.Option("accept", "Accept"), Diag.Option("decline", "Decline")]);
        var projector = new DialoguePresentationProjector("farm.mara", [operation]);
        AiAgent agent = CreateAgent();
        DiagOperationInspection inspection = Diag.Inspect(
            new DiagOperationId(operation.Id),
            DiagOperationKind.Choose);
        agent.Bb.Set(inspection.StartedKey, true);
        agent.Bb.Set(inspection.PendingIdKey, 42L);

        DialoguePresentationSnapshot snapshot = projector.Project(agent, null, 1);

        Assert.Equal("farm.mara", snapshot.DialogueId);
        Assert.Equal("mara.offer", snapshot.OperationId);
        Assert.Equal(DialoguePresentationOperationKind.Choice, snapshot.OperationKind);
        Assert.Equal("Mara", snapshot.SpeakerId);
        Assert.Equal(["accept", "decline"], snapshot.Choices.Select(choice => choice.Id));
        Assert.Equal([0, 1], snapshot.Choices.Select(choice => choice.DeclarationIndex));
        Assert.Equal(1, snapshot.SelectedChoiceIndex);
        Assert.True(snapshot.IsAwaitingChoice);
        Assert.False(snapshot.CanAdvance);
        Assert.Equal(42L, snapshot.PendingOperationId);
    }

    [Fact]
    public void SnapshotAssembly_HasNoRendererDependency()
    {
        string[] dependencies = typeof(DialoguePresentationSnapshot)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(dependencies, name => name.StartsWith("Machina", StringComparison.Ordinal));
        Assert.DoesNotContain(dependencies, name => name.StartsWith("Aurelian", StringComparison.Ordinal));
    }

    private static AiAgent CreateAgent()
    {
        static IEnumerator<AiStep> Idle(AiCtx _)
        {
            while (true)
                yield return null!;
        }

        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = Idle });
        return new AiAgent(new HfsmInstance(graph));
    }
}
