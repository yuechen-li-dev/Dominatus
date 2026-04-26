#nullable enable
using System;
using Dominatus.StrideConn;
using Ariadne.OptFlow.Commands;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Dominatus.StrideSandbox.Scripts.Ui;

public sealed class StrideDialogueSurface : IStrideDialogueSurface
{
    private readonly StrideDialogueState _state = new();
    private readonly Entity _entity;

    private UIComponent? _ui;
    private Entity? _uiHostEntity;
    private Grid? _root;
    private StackPanel? _dialoguePanel;
    private TextBlock? _status;
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

    private string _statusText = "Dominatus installer started";
    private bool _loggedUiAttachment;
    private bool _uiHostAddedToScene;

    public StrideDialogueSurface(Entity entity)
        : this(entity, existingUiComponent: null)
    {
    }

    public StrideDialogueSurface(Entity entity, UIComponent? existingUiComponent)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _ui = existingUiComponent;
    }

    public void EnsureInitialized()
    {
        if (_root is not null)
            return;

        Console.WriteLine("[Dominatus.StrideConn] StrideDialogueSurface.EnsureInitialized entered");

        EnsureUiComponentAttached();

        _status = new TextBlock
        {
            TextColor = Color.Orange,
            Text = _statusText
        };

        _speaker = new TextBlock
        {
            TextColor = Color.Yellow
        };

        _body = new TextBlock
        {
            TextColor = Color.White,
            Text = "Dominatus Stride dialogue surface initialized."
        };

        _prompt = new TextBlock
        {
            TextColor = Color.Aqua,
            Text = "Waiting for first dialogue..."
        };

        _advanceButton = new Button { Content = new TextBlock { Text = "Next (Space/Enter)", TextColor = Color.White } };
        _choicePanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _askInput = new EditText { Text = string.Empty };
        _askSubmitButton = new Button { Content = new TextBlock { Text = "Submit", TextColor = Color.White } };
        _askDefaultButton = new Button { Content = new TextBlock { Text = "Use drop(player);", TextColor = Color.White } };

        _advanceButton.Click += (_, _) => CompleteLine();
        _askSubmitButton.Click += (_, _) => CompleteAsk(_askInput.Text ?? string.Empty);
        _askDefaultButton.Click += (_, _) => CompleteAsk("drop(player);");

        _dialoguePanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 420,
            BackgroundColor = new Color(0, 0, 0, 230),
            Visibility = Visibility.Collapsed
        };

        _dialoguePanel.Children.Add(_speaker);
        _dialoguePanel.Children.Add(_body);
        _dialoguePanel.Children.Add(_prompt);
        _dialoguePanel.Children.Add(_choicePanel);
        _dialoguePanel.Children.Add(_askInput);
        _dialoguePanel.Children.Add(_askSubmitButton);
        _dialoguePanel.Children.Add(_askDefaultButton);
        _dialoguePanel.Children.Add(_advanceButton);

        _root = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            BackgroundColor = new Color(20, 30, 60, 40)
        };
        _root.Children.Add(_status);
        _root.Children.Add(_dialoguePanel);

        _ui!.Enabled = true;
        _ui.Page = new UIPage { RootElement = _root };

        LogUiDiagnostics();
        Console.WriteLine("[Dominatus.StrideConn] StrideDialogueSurface initialized");

        Refresh();
    }

    public void SetStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return;

        _statusText = status;
        if (_status is not null)
            _status.Text = status;
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

        Console.WriteLine($"[Dominatus.StrideConn] TryShowLine called with speaker='{command.Speaker ?? string.Empty}', text='{command.Text}'");
        SetStatus($"DiagLine received: {command.Text}");
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

        Console.WriteLine($"[Dominatus.StrideConn] TryShowChoose called with prompt='{command.Prompt}', optionCount={command.Options.Count}");
        SetStatus($"Choose received: {command.Prompt}");
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

        Console.WriteLine($"[Dominatus.StrideConn] TryShowAsk called with prompt='{command.Prompt}'");
        SetStatus($"Ask received: {command.Prompt}");
        _state.ShowAsk(command);
        _onAdvance = null;
        _onChoose = null;
        _onAsk = onSubmit;
        Refresh();
        return true;
    }

    private void EnsureUiComponentAttached()
    {
        if (_ui is not null)
        {
            if (_entity.Get<UIComponent>() is null)
                _entity.Components.Add(_ui);

            return;
        }

        _ui = _entity.Get<UIComponent>();
        if (_ui is not null)
            return;

        if (_entity.Scene is null)
        {
            // Fallback for tests or unusual initialization order before the entity has a Scene.
            _ui = new UIComponent();
            _entity.Components.Add(_ui);
            return;
        }

        _uiHostEntity = new Entity("DominatusDialogueUiHost");
        _ui = new UIComponent();
        _uiHostEntity.Components.Add(_ui);
        _uiHostEntity.Transform.Parent = _entity.Transform;
        _entity.Scene.Entities.Add(_uiHostEntity);
        _uiHostAddedToScene = true;
    }

    private void LogUiDiagnostics()
    {
        if (_loggedUiAttachment || _ui is null)
            return;

        var ui = _ui;
        _loggedUiAttachment = true;
        Console.WriteLine($"[Dominatus.StrideConn] UIComponent attached: {ui is not null}");
        Console.WriteLine($"[Dominatus.StrideConn] UIComponent.Enabled: {ui!.Enabled}");
        Console.WriteLine($"[Dominatus.StrideConn] UIComponent.Page != null: {ui.Page is not null}");
        Console.WriteLine($"[Dominatus.StrideConn] UIPage.RootElement != null: {ui.Page?.RootElement is not null}");

        if (_root is not null)
        {
            Console.WriteLine($"[Dominatus.StrideConn] Root alignment: H={_root.HorizontalAlignment}, V={_root.VerticalAlignment}");
            Console.WriteLine($"[Dominatus.StrideConn] Root visibility: {_root.Visibility}");
        }

        if (_uiHostEntity is not null)
        {
            Console.WriteLine($"[Dominatus.StrideConn] UIComponent hosted by child entity '{_uiHostEntity.Name}'");
            Console.WriteLine($"[Dominatus.StrideConn] UI host added to scene: {_uiHostAddedToScene}");
        }
    }

    private void CompleteLine()
    {
        if (_state.Mode != DialogueMode.Line)
            return;

        Console.WriteLine("[Dominatus.StrideConn] line completed");
        SetStatus("Waiting for first dialogue...");
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

        Console.WriteLine($"[Dominatus.StrideConn] choice completed: {choice}");
        SetStatus("Waiting for first dialogue...");
        var callback = _onChoose;
        ResetSurface();
        callback?.Invoke(choice);
    }

    private void CompleteAsk(string answer)
    {
        if (_state.Mode != DialogueMode.Ask)
            return;

        var resolved = string.IsNullOrWhiteSpace(answer) ? "drop(player);" : answer;
        Console.WriteLine($"[Dominatus.StrideConn] ask completed: {resolved}");
        SetStatus("Waiting for first dialogue...");
        var callback = _onAsk;
        ResetSurface();
        callback?.Invoke(resolved);
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
        if (_ui is null || _status is null || _dialoguePanel is null || _speaker is null || _body is null || _prompt is null || _advanceButton is null || _choicePanel is null || _askInput is null || _askSubmitButton is null || _askDefaultButton is null)
            return;

        _ui.Enabled = true;
        _status.Text = _statusText;

        _speaker.Text = string.IsNullOrWhiteSpace(_state.Speaker) ? string.Empty : $"[{_state.Speaker}]";
        _body.Text = _state.BodyText;
        _prompt.Text = _state.Prompt;

        _dialoguePanel.Visibility = _state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
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
                    Content = new TextBlock { Text = $"{i + 1}. {option.Text}", TextColor = Color.White }
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
