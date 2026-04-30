using Ariadne.OptFlow.Commands;

namespace Dominatus.StrideConn;

public sealed class StrideDialogueState
{
    public DialogueMode Mode { get; private set; } = DialogueMode.Hidden;
    public string Speaker { get; private set; } = string.Empty;
    public string BodyText { get; private set; } = string.Empty;
    public string Prompt { get; private set; } = string.Empty;
    public IReadOnlyList<DiagChoice> Options { get; private set; } = [];
    public string AskValue { get; set; } = string.Empty;

    public bool IsBusy => Mode != DialogueMode.Hidden;

    public void ShowLine(DiagLineCommand command)
    {
        Mode = DialogueMode.Line;
        Speaker = command.Speaker ?? string.Empty;
        BodyText = command.Text;
        Prompt = string.Empty;
        Options = [];
        AskValue = string.Empty;
    }

    public void ShowChoose(DiagChooseCommand command)
    {
        Mode = DialogueMode.Choose;
        Speaker = string.Empty;
        BodyText = string.Empty;
        Prompt = command.Prompt;
        Options = command.Options;
        AskValue = string.Empty;
    }

    public void ShowAsk(DiagAskCommand command)
    {
        Mode = DialogueMode.Ask;
        Speaker = string.Empty;
        BodyText = string.Empty;
        Prompt = command.Prompt;
        Options = [];
        AskValue = string.Empty;
    }

    public void Hide()
    {
        Mode = DialogueMode.Hidden;
        Speaker = string.Empty;
        BodyText = string.Empty;
        Prompt = string.Empty;
        Options = [];
        AskValue = string.Empty;
    }
}

public enum DialogueMode
{
    Hidden,
    Line,
    Choose,
    Ask
}
