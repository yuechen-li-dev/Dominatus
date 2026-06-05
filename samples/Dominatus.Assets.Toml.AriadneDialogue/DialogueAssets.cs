namespace Dominatus.Assets.Toml.AriadneDialogue;

public sealed record DialogueAsset
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Start { get; init; }

    public required Dictionary<string, DialogueNodeAsset> Nodes { get; init; }
}

public sealed record DialogueNodeAsset
{
    public required string Speaker { get; init; }

    public string? Line { get; init; }

    public string? Text { get; init; }

    public List<string> Tags { get; init; } = [];

    public List<DialogueChoiceAsset> Choices { get; init; } = [];

    public string? Condition { get; init; }

    public List<DialogueEffectAsset> Effects { get; init; } = [];
}

public sealed record DialogueChoiceAsset
{
    public required string Id { get; init; }

    public string? Line { get; init; }

    public string? Text { get; init; }

    public string? Next { get; init; }

    public string? NextAsset { get; init; }

    public string? NextNode { get; init; }
}

public sealed record DialogueEffectAsset
{
    public required string Id { get; init; }

    public string? Value { get; init; }
}
