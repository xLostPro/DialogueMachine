using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

public sealed partial class MainWindow
{
    private SceneNavigation navigation = new(new DialogueProject(), []);
    private bool updatingOutlineFilters;
    private sealed record FilterOption(string Label, string? Value) { public override string ToString() => Label; }
    private OutlineFilter CurrentOutlineFilter() => new(C<TextBox>("SearchBox").Text ?? "", (C<ComboBox>("SpeakerFilter").SelectedItem as FilterOption)?.Value, (C<ComboBox>("TagFilter").SelectedItem as FilterOption)?.Value,
        C<ComboBox>("IssueFilter").SelectedIndex switch { 1 => SceneIssueFilter.WithIssues, 2 => SceneIssueFilter.Errors, 3 => SceneIssueFilter.Warnings, 4 => SceneIssueFilter.WithoutIssues, _ => SceneIssueFilter.All }, (C<ComboBox>("GroupFilter").SelectedItem as FilterOption)?.Value);
    private void RefreshOutlineFilters()
    {
        updatingOutlineFilters = true;
        void Populate(string name, string label, IReadOnlyList<string> values)
        {
            var combo = C<ComboBox>(name); var selectedValue = (combo.SelectedItem as FilterOption)?.Value;
            var options = new[] { new FilterOption(label, null) }.Concat(values.Select(v => new FilterOption(v, v))).ToList();
            combo.ItemsSource = options; combo.SelectedItem = options.FirstOrDefault(o => o.Value == selectedValue) ?? options[0];
        }
        Populate("SpeakerFilter", "Any speaker", navigation.Speakers); Populate("TagFilter", "Any tag", navigation.Tags);
        var groupCombo = C<ComboBox>("GroupFilter"); var groupId = (groupCombo.SelectedItem as FilterOption)?.Value;
        var groupOptions = new List<FilterOption> { new("Any group", null), new("Ungrouped scenes", "") };
        groupOptions.AddRange(project.SceneGroups.Select(g => new FilterOption(g.Name, g.Id)));
        groupCombo.ItemsSource = groupOptions; groupCombo.SelectedItem = groupOptions.FirstOrDefault(o => o.Value == groupId) ?? groupOptions[0];
        updatingOutlineFilters = false; RefreshOutline();
    }
    private void ClearOutlineFilters()
    {
        updatingOutlineFilters = true;
        C<TextBox>("SearchBox").Text = ""; C<ComboBox>("SpeakerFilter").SelectedIndex = 0; C<ComboBox>("TagFilter").SelectedIndex = 0; C<ComboBox>("IssueFilter").SelectedIndex = 0; C<ComboBox>("GroupFilter").SelectedIndex = 0;
        updatingOutlineFilters = false; RefreshOutline();
    }
    private Control Destination(DialogueState scene, string? value, Action<string?> apply, int? choiceIndex = null)
    {
        var label = choiceIndex is { } i ? $"Choice {i + 1} destination" : "Continue destination";
        var resolved = navigation.Resolve(value);
        var currentLabel = string.IsNullOrWhiteSpace(value) ? choiceIndex is not null ? "Use scene's continue destination" : "No continue destination"
            : value == "[EXIT]" ? "End conversation" : resolved is not null ? resolved.Title + " [" + resolved.Id + "]" : "Missing: " + value;
        var choose = Ui.AsyncButton("Choose…", async () =>
        {
            Commit();
            if (await PickDestination(value, choiceIndex is not null, label) is { } next && next.Id != value)
                Mutate(() => apply(next.Id));
        }); AutomationProperties.SetName(choose, "Choose " + label.ToLowerInvariant());
        var effective = choiceIndex is not null && string.IsNullOrWhiteSpace(value) ? scene.DefaultNextId : value;
        var target = navigation.Resolve(effective);
        var jump = Ui.Button("Jump →", () => { if (target is not null) SelectScene(project.ListOfCharacterStates[target.Index], true); }, "subtle");
        AutomationProperties.SetName(jump, "Jump to " + label.ToLowerInvariant()); jump.IsEnabled = target is not null;
        ToolTip.SetTip(jump, target is null ? "Choose a scene destination to jump to it." : "Open " + target.Title + " [" + target.Id + "]");
        return Ui.Stack(Ui.Text(currentLabel, 13), Ui.Row(choose, jump));
    }
    private async Task<DestinationOption?> PickDestination(string? current, bool allowFallback, string label)
    {
        var options = navigation.Destinations(allowFallback, current);
        var search = new TextBox { Name = "DestinationSearch", PlaceholderText = "Search scene titles or IDs…", PlaceholderForeground = Ui.Brush("Muted") }; AutomationProperties.SetName(search, "Search destinations");
        var list = new ListBox { Name = "DestinationMatches", Focusable = true }; AutomationProperties.SetName(list, "Destination results");
        var summary = Ui.Text("", 12, true); summary.Name = "DestinationSummary"; AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        Window? window = null; var choose = Ui.Button("Choose destination", () => window!.Close(list.SelectedItem as DestinationOption), "primary"); choose.IsDefault = true; choose.Name = "ChooseDestination";
        void Filter()
        {
            var results = SceneNavigation.FindDestinations(options, search.Text ?? ""); list.ItemsSource = results.Matches;
            list.SelectedItem = results.Matches.FirstOrDefault(o => o.Id == (string.IsNullOrWhiteSpace(current) ? null : current)) ?? results.Matches.FirstOrDefault();
            choose.IsEnabled = results.TotalCount > 0;
            summary.Text = results.TotalCount == 0 ? "No matching destinations. Try another title or ID." : $"{results.TotalCount} {(results.TotalCount == 1 ? "destination" : "destinations")}" + (results.TotalCount > results.Matches.Count ? $" · showing the first {results.Matches.Count}. Type to narrow the results." : "");
        }
        search.TextChanged += (_, _) => Filter(); Filter();
        search.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Down && list.SelectedItem is not null) { list.Focus(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is DestinationOption destination) window!.Close(destination); };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(24) };
        content.Children.Add(Ui.Text(label, 23)); search.Margin = new Thickness(0,14,0,10); Grid.SetRow(search, 1); content.Children.Add(search);
        summary.Margin = new Thickness(0,0,0,10); Grid.SetRow(summary, 2); content.Children.Add(summary); Grid.SetRow(list, 3); content.Children.Add(list);
        var buttons = Ui.Row(Ui.Button("Cancel", () => window!.Close()), choose); buttons.Margin = new Thickness(0,12,0,0); Grid.SetRow(buttons, 4); content.Children.Add(buttons);
        window = Ui.Dialog(this, "Choose destination", content, 610, 560, preferences.Scale); window.Opened += (_, _) => search.Focus();
        return await window.ShowDialog<DestinationOption?>(this);
    }
    private void AddSceneConnections(StackPanel panel, DialogueState scene)
    {
        var index = project.ListOfCharacterStates.IndexOf(scene);
        panel.Children.Add(Ui.Text("Outgoing connections", 15));
        foreach (var link in navigation.Outgoing(index))
        {
            if (link.TargetSceneIndex is { } targetIndex)
            {
                var target = navigation.Scenes[targetIndex];
                var button = Ui.Button(link.Label + " → " + target.Title + " [" + target.Id + "]", () => SelectScene(project.ListOfCharacterStates[targetIndex], true), "subtle");
                button.Content = Ui.Text(link.Label + " → " + target.Title + " [" + target.Id + "]", 13); button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left; panel.Children.Add(button);
            }
            else panel.Children.Add(Ui.Text(link.Label + " → " + (link.TargetId == "[EXIT]" ? "End conversation" : string.IsNullOrWhiteSpace(link.TargetId) ? "No destination" : "Missing: " + link.TargetId), 12, true));
        }
        panel.Children.Add(Ui.Text("Incoming connections", 15)); var sources = navigation.Incoming(index);
        if (sources.Count == 0) panel.Children.Add(Ui.Text("No other scenes point here yet.", 12, true));
        foreach (var source in sources)
        {
            var button = Ui.Button("← " + source.Title + " [" + source.Id + "]", () => SelectScene(project.ListOfCharacterStates[source.Index], true), "subtle");
            button.Content = Ui.Text("← " + source.Title + " [" + source.Id + "]", 13); button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left; panel.Children.Add(button);
        }
    }
}
