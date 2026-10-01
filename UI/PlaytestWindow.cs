using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed class PlaytestWindow : Window
{
    private readonly DialogueProject original;
    private readonly string? start;
    private Simulation simulation;
    private readonly StackPanel story = new() { Spacing = 17 };
    private readonly StackPanel variablePanel = new() { Spacing = 10 };
    private readonly StackPanel eventPanel = new() { Spacing = 8 };
    private readonly TextBlock location = Ui.Text("", 12, true);
    private readonly Button back;
    public Simulation Session => simulation;
    public PlaytestWindow(DialogueProject project, string? startId)
    {
        original = ProjectStore.Clone(project); start = startId; simulation = new Simulation(original, start);
        Title = "Playtest · " + project.ProjectName; Width = 1040; Height = 770; MinWidth = 820; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { RowDefinitions = new RowDefinitions("76,*,Auto"), ColumnDefinitions = new ColumnDefinitions("*,300") };
        var title = Ui.Stack(Ui.Text("PLAYTEST", 11, true), Ui.Text(project.ProjectName, 22));
        back = Ui.Button("← Back", () => { simulation.Back(); Refresh(); });
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(26,15) }; header.Children.Add(title);
        var tools = Ui.Row(back, Ui.Button("↺ Restart", () => { simulation = new Simulation(original, start); Refresh(); }), Ui.AsyncButton("Export transcript", ExportTranscript)); Grid.SetColumn(tools, 1); header.Children.Add(tools); Grid.SetColumnSpan(header, 2); grid.Children.Add(header);
        var viewport = new ScrollViewer { Content = story, Margin = new Thickness(38,28) }; Grid.SetRow(viewport, 1); grid.Children.Add(viewport);
        var sidebar = Ui.Stack(Ui.Text("LIVE VARIABLES", 11, true), Ui.Text("Adjust values to test different paths.", 12, true), variablePanel, Ui.Text("INTEGRATION EVENTS", 11, true), Ui.Text("Events, scripts and shops are logged here. Game code is not executed.", 11, true), eventPanel);
        var right = new Border { Background = Ui.Brush("Panel"), BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(1,0,0,0), Padding = new Thickness(22), Child = new ScrollViewer { Content = sidebar } }; Grid.SetColumn(right, 1); Grid.SetRow(right, 1); grid.Children.Add(right);
        var footer = new Border { Background = Ui.Brush("Panel"), Padding = new Thickness(22,12), Child = Ui.Row(location, Ui.Text("Escape closes playtest", 11, true)) }; Grid.SetRow(footer, 2); Grid.SetColumnSpan(footer, 2); grid.Children.Add(footer); Content = grid;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } }; Refresh();
    }
    private void Refresh(bool rebuildVariables = true)
    {
        story.Children.Clear(); back.IsEnabled = simulation.CanBack;
        if (simulation.Current is not { } current)
        {
            story.Children.Add(Ui.Text("○", 50, true)); story.Children.Add(Ui.Text("The conversation ends here.", 28)); story.Children.Add(Ui.Text("Try another path, change the starting values, or return to your story to keep writing.", 15, true));
            story.Children.Add(Ui.Button("Restart story", () => { simulation = new Simulation(original, start); Refresh(); }, "play")); location.Text = "Conversation complete";
        }
        else
        {
            location.Text = current.State + "  ·  Page " + (simulation.PageIndex + 1) + " of " + simulation.Pages.Length;
            var name = Ui.Text(current.Speaker.ToUpperInvariant(), 12); name.Foreground = Ui.Brush("MintInk"); name.LetterSpacing = 1.5; story.Children.Add(name);
            story.Children.Add(RichDialogue.Create(simulation.Pages[simulation.PageIndex]));
            if (!current.SayItAll) story.Children.Add(Ui.Button("Reroll line", () => { simulation.RerollLine(); Refresh(); }, "subtle"));
            if (current.Type == "Shop") story.Children.Add(Ui.Card(Ui.Text("Shop preview · " + current.ShopData.ShopId + "\nYour game handles this shop after export.", 13, true)));
            if (simulation.MorePages) story.Children.Add(Ui.Button("Continue →", () => { simulation.NextPage(); Refresh(); }, "primary"));
            else
            {
                var available = 0;
                foreach (var choice in current.Choices)
                {
                    var met = DialogueEngine.RequirementsMet(choice, simulation.Variables, out var reason) || choice.Visibility == VisibilityMode.Always;
                    if (!met && choice.Visibility == VisibilityMode.Hide) continue;
                    if (met) available++;
                    var c = choice;
                    var label = DialogueEngine.PlainText(DialogueEngine.ResolveText(choice.ButtonText, simulation.Variables, original.TextVariables));
                    var button = Ui.Button("↳  " + label, () => Transition(() => simulation.Choose(c)));
                    button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Content = Ui.Text("↳  " + label, 15); button.IsEnabled = met;
                    if (!met) ToolTip.SetTip(button, reason);
                    story.Children.Add(button); if (!met) story.Children.Add(Ui.Text(reason, 11, true));
                }
                if (current.Choices.Count == 0 || available == 0)
                    if (current.DefaultNextId is not null) story.Children.Add(Ui.Button(current.DefaultNextId == "[EXIT]" ? "End conversation →" : "Continue →", () => Transition(simulation.Continue), "primary"));
                if (available == 0 && current.Choices.Count > 0 && current.DefaultNextId is null) story.Children.Add(Ui.Text("No response is available with these values. Adjust the live variables or go back.", 13, true));
            }
        }
        if (rebuildVariables)
        {
            variablePanel.Children.Clear();
            foreach (var key in simulation.Variables.Keys.Order().ToArray())
            {
                var k = key; var input = new NumericUpDown { Value = simulation.Variables[k], Minimum = k.StartsWith("inv:") ? 0 : int.MinValue, Maximum = int.MaxValue, FormatString = "0", Increment = 1 };
                input.ValueChanged += (_, _) => { if (input.Value is { } v) { simulation.SetVariable(k, (int)v); Refresh(false); } };
                variablePanel.Children.Add(Ui.Field(k, input));
            }
            if (simulation.Variables.Count == 0) variablePanel.Children.Add(Ui.Text("This story has no numeric variables.", 12, true));
        }
        eventPanel.Children.Clear(); foreach (var entry in simulation.Events.TakeLast(12)) eventPanel.Children.Add(Ui.Text(entry, 12, true));
        if (simulation.Events.Count == 0) eventPanel.Children.Add(Ui.Text("No integration events yet.", 12, true));
    }
    private void Transition(Action action)
    {
        try { action(); Refresh(); } catch (InvalidOperationException ex) { story.Children.Add(Ui.Text(ex.Message, 13, true)); }
    }
    private async Task ExportTranscript()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export this playtest", SuggestedFileName = "playtest-transcript.txt", DefaultExtension = "txt", ShowOverwritePrompt = true });
        if (file?.TryGetLocalPath() is not { } path) return;
        try { ProjectStore.AtomicWrite(path, original.ProjectName + "\n\n" + string.Join("\n\n", simulation.Transcript) + "\n\nFinal variables\n" + string.Join("\n", simulation.Variables.Select(v => v.Key + " = " + v.Value)) + "\n\nIntegration events\n" + string.Join("\n", simulation.Events)); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { await Ui.Ask(this, "Could not export transcript", ex.Message, "Close"); }
    }
}
