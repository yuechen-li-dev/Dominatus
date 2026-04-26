using Ariadne.OptFlow.Commands;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Dominatus.StrideConn;

public sealed class StrideDialogueSurface : IStrideDialogueSurface
{
    private readonly StrideDialogueState _state = new();
    private readonly Entity _entity;

    private UIComponent? _ui;
    private Grid? _root;
    private TextBlock? _speaker;
    private TextBlock? _body;
    private TextBlock? _prompt;
    private Button? _advanceButton;
    private StackPanel? _choicePanel;
    private EditText? _askInput;
    private Button? _askSubmitButton;
    private Button? _askDefaultButton;

    private Action? _onAdvance;
    private Action<string>? _onChoose;
    private Action<string>? _onAsk;

    public StrideDialogueSurface(Entity entity)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
    }

    public void EnsureInitialized()
    {
        if (_ui is not null)
            return;

        _ui = _entity.Get<UIComponent>() ?? new UIComponent();
        if (_entity.Get<UIComponent>() is null)
            _entity.Components.Add(_ui);

        _speaker = new TextBlock();
        _body = new TextBlock();
        _prompt = new TextBlock();
        _advanceButton = new Button { Content = new TextBlock { Text = "Next (Space/Enter)" } };
        _choicePanel = new StackPanel();
        _askInput = new EditText { Text = "" };
        _askSubmitButton = new Button { Content = new TextBlock { Text = "Submit" } };
        _askDefaultButton = new Button { Content = new TextBlock { Text = "Use drop(player);" } };

        _advanceButton.Click += (_, _) => CompleteLine();
        _askSubmitButton.Click += (_, _) => CompleteAsk(_askInput.Text ?? string.Empty);
        _askDefaultButton.Click += (_, _) => CompleteAsk("drop(player);");

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        panel.Children.Add(_speaker);
        panel.Children.Add(_body);
        panel.Children.Add(_prompt);
        panel.Children.Add(_choicePanel);
        panel.Children.Add(_askInput);
        panel.Children.Add(_askSubmitButton);
        panel.Children.Add(_askDefaultButton);
        panel.Children.Add(_advanceButton);

        _root = new Grid
        {
            BackgroundColor = new Color(10, 10, 10, 180)
        };
        _root.Children.Add(panel);

        _ui.Page = new UIPage { RootElement = _root };
        Refresh();
    }

    public void Update(InputManager input)
    {
        if (input is null || !_state.IsBusy)
            return;

        if (_state.Mode == DialogueMode.Line && (input.IsKeyPressed(Keys.Space) || input.IsKeyPressed(Keys.Enter)))
            CompleteLine();

        if (_state.Mode == DialogueMode.Choose)
        {
            if (input.IsKeyPressed(Keys.D1)) TryChooseAt(0);
            if (input.IsKeyPressed(Keys.D2)) TryChooseAt(1);
            if (input.IsKeyPressed(Keys.D3)) TryChooseAt(2);
            if (input.IsKeyPressed(Keys.D4)) TryChooseAt(3);
            if (input.IsKeyPressed(Keys.D5)) TryChooseAt(4);
            if (input.IsKeyPressed(Keys.D6)) TryChooseAt(5);
            if (input.IsKeyPressed(Keys.D7)) TryChooseAt(6);
            if (input.IsKeyPressed(Keys.D8)) TryChooseAt(7);
            if (input.IsKeyPressed(Keys.D9)) TryChooseAt(8);
        }

        if (_state.Mode == DialogueMode.Ask && input.IsKeyPressed(Keys.Enter))
            CompleteAsk(_askInput?.Text ?? _state.AskValue);
    }

    public bool TryShowLine(DiagLineCommand command, Action onAdvance)
    {
        if (_state.IsBusy)
            return false;

        _state.ShowLine(command);
        _onAdvance = onAdvance;
        _onChoose = null;
        _onAsk = null;
        Refresh();
        return true;
    }

    public bool TryShowChoose(DiagChooseCommand command, Action<string> onChoose)
    {
        if (_state.IsBusy)
            return false;

        _state.ShowChoose(command);
        _onAdvance = null;
        _onChoose = onChoose;
        _onAsk = null;
        Refresh();
        return true;
    }

    public bool TryShowAsk(DiagAskCommand command, Action<string> onSubmit)
    {
        if (_state.IsBusy)
            return false;

        _state.ShowAsk(command);
        _onAdvance = null;
        _onChoose = null;
        _onAsk = onSubmit;
        Refresh();
        return true;
    }

    private void CompleteLine()
    {
        if (_state.Mode != DialogueMode.Line)
            return;

        var callback = _onAdvance;
        ResetSurface();
        callback?.Invoke();
    }

    private void TryChooseAt(int index)
    {
        if (_state.Mode != DialogueMode.Choose)
            return;

        if (index < 0 || index >= _state.Options.Count)
            return;

        var option = _state.Options[index];
        CompleteChoose(option.Key);
    }

    private void CompleteChoose(string choice)
    {
        if (_state.Mode != DialogueMode.Choose)
            return;

        var callback = _onChoose;
        ResetSurface();
        callback?.Invoke(choice);
    }

    private void CompleteAsk(string answer)
    {
        if (_state.Mode != DialogueMode.Ask)
            return;

        var callback = _onAsk;
        ResetSurface();
        callback?.Invoke(string.IsNullOrWhiteSpace(answer) ? "drop(player);" : answer);
    }

    private void ResetSurface()
    {
        _state.Hide();
        _onAdvance = null;
        _onChoose = null;
        _onAsk = null;
        Refresh();
    }

    private void Refresh()
    {
        if (_ui is null || _speaker is null || _body is null || _prompt is null || _advanceButton is null || _choicePanel is null || _askInput is null || _askSubmitButton is null || _askDefaultButton is null)
            return;

        _ui.Enabled = _state.IsBusy;

        _speaker.Text = string.IsNullOrWhiteSpace(_state.Speaker) ? string.Empty : $"[{_state.Speaker}]";
        _body.Text = _state.BodyText;
        _prompt.Text = _state.Prompt;

        _advanceButton.Visibility = _state.Mode == DialogueMode.Line ? Visibility.Visible : Visibility.Collapsed;
        _choicePanel.Visibility = _state.Mode == DialogueMode.Choose ? Visibility.Visible : Visibility.Collapsed;
        _askInput.Visibility = _state.Mode == DialogueMode.Ask ? Visibility.Visible : Visibility.Collapsed;
        _askSubmitButton.Visibility = _state.Mode == DialogueMode.Ask ? Visibility.Visible : Visibility.Collapsed;
        _askDefaultButton.Visibility = _state.Mode == DialogueMode.Ask ? Visibility.Visible : Visibility.Collapsed;

        _choicePanel.Children.Clear();
        if (_state.Mode == DialogueMode.Choose)
        {
            for (var i = 0; i < _state.Options.Count; i++)
            {
                var option = _state.Options[i];
                var button = new Button
                {
                    Content = new TextBlock { Text = $"{i + 1}. {option.Text}" }
                };
                var key = option.Key;
                button.Click += (_, _) => CompleteChoose(key);
                _choicePanel.Children.Add(button);
            }
        }

        if (_state.Mode == DialogueMode.Ask)
            _askInput.Text = _state.AskValue;
    }
}
