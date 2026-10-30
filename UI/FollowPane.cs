using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using DialogueMachine.Core.LiveLink;

namespace DialogueMachine.UI;

// Shows the followed game, using the playtest window's variable and event layout.
internal sealed class FollowPane : Border
{
    public event Action<string>? RunnerPicked;
    public event Action<string, int>? NumberEdited;
    public event Action<string, string>? TextEdited;
    public event Action? PushRequested, StopRequested, HideRequested;
    public bool AutoPan => autoPan?.IsChecked == true;

    private ComboBox runners = null!;
    private TextBlock notice = null!, location = null!, speaker = null!, ending = null!;
    private Button push = null!;
    private CheckBox? autoPan;
    private ContentControl line = null!;
    private StackPanel variables = null!, eventPanel = null!;
    private readonly Dictionary<string, Control> inputs = new(StringComparer.Ordinal);
    private List<string> runnerKeys = [];
    private IBrush? paletteInk;
    private bool updating;

    public FollowPane() { Name = "FollowPane"; IsVisible = false; Build(); }

    // Palette brushes are read once, so a theme change rebuilds the controls.
    private void Build()
    {
        paletteInk = Ui.Brush("Ink"); Background = Ui.Brush("Panel"); BorderBrush = Ui.Brush("Line"); BorderThickness = new Thickness(1, 0, 0, 0);
        var pan = autoPan?.IsChecked ?? true;
        runners = new ComboBox { Name = "FollowRunner", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch }; runnerKeys = [];
        AutomationProperties.SetName(runners, "Followed game");
        runners.SelectionChanged += (_, _) => { if (!updating && runners.SelectedIndex >= 0 && runners.SelectedIndex < runnerKeys.Count) RunnerPicked?.Invoke(runnerKeys[runners.SelectedIndex]); };
        notice = Ui.Text("", 13); notice.Name = "FollowNotice"; notice.Foreground = Ui.Brush("GraphLive"); notice.IsVisible = false; AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        push = Ui.Button("Push update", () => PushRequested?.Invoke(), "primary"); push.Name = "FollowPush"; push.IsVisible = false;
        location = Ui.Text("", 12, true); location.Name = "FollowLocation";
        speaker = Ui.Text("", 12); speaker.Foreground = Ui.Brush("MintInk"); speaker.LetterSpacing = 1.5; speaker.Name = "FollowSpeaker";
        ending = Ui.Text("", 13, true); ending.Name = "FollowEnding"; ending.IsVisible = false; AutomationProperties.SetLiveSetting(ending, AutomationLiveSetting.Polite);
        autoPan = new CheckBox { Name = "FollowAutoPan", Content = Ui.Text("Auto-pan to the current scene", 13), IsChecked = pan };
        AutomationProperties.SetName(autoPan, "Auto-pan to the current scene");
        var stop = Ui.Button("Stop following", () => StopRequested?.Invoke()); stop.Name = "StopFollowing";
        var hide = Ui.Button("Hide", () => HideRequested?.Invoke(), "subtle"); hide.Name = "HideFollow";
        line = new(); variables = new() { Spacing = 10 }; eventPanel = new() { Spacing = 8 }; inputs.Clear();
        var content = Ui.Stack(Ui.Text("LIVE FOLLOW", 11, true), runners, notice, push, location, speaker, line, ending, autoPan, Ui.Row(stop, hide),
            Ui.Text("LIVE VARIABLES", 11, true), Ui.Text("Edits are sent to the running game. Your story doesn't change.", 12, true), variables,
            Ui.Text("INTEGRATION EVENTS", 11, true), eventPanel);
        content.Margin = new Thickness(22); content.Spacing = 10;
        Child = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    public void Update(LiveFollow follow, LiveFollowStatus status, string? speakerName, string? text)
    {
        if (!ReferenceEquals(paletteInk, Ui.Brush("Ink"))) Build();
        updating = true;
        var keys = follow.Runners.Select(r => r.Key).ToList();
        if (!keys.SequenceEqual(runnerKeys)) { runnerKeys = keys; runners.ItemsSource = follow.Runners.Select(r => r.Label).ToList(); }
        runners.SelectedIndex = follow.Followed is { } followed ? keys.IndexOf(followed.Key) : -1;
        notice.Text = status.Notice; notice.IsVisible = status.Notice is not null; push.IsVisible = status.Outdated;
        var runner = follow.Followed;
        location.Text = runner is null ? "Start dialogue in a game that has the plugin. It will appear here." : follow.CurrentSceneId is { } scene ? $"{scene}  ·  Page {runner.PageIndex + 1}" : "No scene yet";
        speaker.Text = speakerName?.ToUpperInvariant(); speaker.IsVisible = speakerName is not null;
        line.Content = text is null ? null : RichDialogue.Create(text, 17);
        ending.Text = follow.Disconnected ? "Game disconnected" : follow.Ended ? "Conversation ended" : null; ending.IsVisible = ending.Text is not null;
        UpdateVariables(runner);
        eventPanel.Children.Clear();
        foreach (var entry in follow.Events.TakeLast(12)) eventPanel.Children.Add(Ui.Text(entry, 12, true));
        if (follow.Events.Count == 0) eventPanel.Children.Add(Ui.Text("No integration events yet.", 12, true));
        updating = false;
    }

    private void UpdateVariables(LiveRunnerState? runner)
    {
        var numbers = runner?.Numeric ?? new Dictionary<string, int>(); var texts = runner?.Text ?? new Dictionary<string, string>();
        var keys = numbers.Keys.Order(StringComparer.Ordinal).Concat(texts.Keys.Order(StringComparer.Ordinal).Select(k => k + " (text)")).ToList();
        if (!keys.SequenceEqual(inputs.Keys))
        {
            variables.Children.Clear(); inputs.Clear();
            foreach (var key in numbers.Keys.Order(StringComparer.Ordinal))
            {
                var k = key; var input = new NumericUpDown { Value = numbers[k], Minimum = k.StartsWith("inv:", StringComparison.Ordinal) ? 0 : int.MinValue, Maximum = int.MaxValue, FormatString = "0", Increment = 1 };
                input.ValueChanged += (_, _) => { if (!updating && input.Value is { } v) NumberEdited?.Invoke(k, (int)v); };
                inputs[k] = input; variables.Children.Add(Ui.Field(k, input));
            }
            foreach (var key in texts.Keys.Order(StringComparer.Ordinal))
            {
                var k = key; var input = Ui.Input(texts[k], value => { if (!updating) TextEdited?.Invoke(k, value); });
                inputs[k + " (text)"] = input; variables.Children.Add(Ui.Field(k + " (text)", input));
            }
            if (inputs.Count == 0) variables.Children.Add(Ui.Text("No variables reported.", 12, true));
            return;
        }
        // Leave the field being typed in alone; the game's next state confirms the edit.
        foreach (var (key, value) in numbers) if (inputs[key] is NumericUpDown { IsKeyboardFocusWithin: false } input) input.Value = value;
        foreach (var (key, value) in texts) if (inputs[key + " (text)"] is TextBox { IsKeyboardFocusWithin: false } input) input.Text = value;
    }
}
