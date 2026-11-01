using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using DialogueMachine.Core;

namespace DialogueMachine.UI;

/// <summary>What the find &amp; replace dialog needs from its host window. Searching and replacing stay in Core; the view only builds controls.</summary>
internal sealed record SearchActions(Action<Action> Mutate, Action<TextMatch> GoToMatch);

/// <summary>The Find &amp; replace dialog.</summary>
internal sealed class SearchView(Func<DialogueProject> getProject, SearchActions actions)
{
    public async Task Show(Window owner, double scale, int? sceneIndex)
    {
        var project = getProject();
        var query = new TextBox { Name = "FindQuery", PlaceholderText = "Find text…", PlaceholderForeground = Ui.Brush("Muted") };
        var replacement = new TextBox { Name = "FindReplacement", PlaceholderText = "Replace with… (empty removes matches)", PlaceholderForeground = Ui.Brush("Muted") };
        var matchCase = new CheckBox { Content = "Match case", Name = "FindMatchCase" };
        var wholeWord = new CheckBox { Content = "Whole word", Name = "FindWholeWord" };
        var scope = new ComboBox { Name = "FindScope", ItemsSource = new[] { "Entire project", "Selected scene" }, SelectedIndex = 0 };
        scope.IsEnabled = sceneIndex is not null;
        var fields = new Dictionary<SearchFields, CheckBox>(); var fieldRow = new WrapPanel();
        foreach (var (field, label) in new[] { (SearchFields.Dialogue, "Dialogue"), (SearchFields.Responses, "Responses"), (SearchFields.Titles, "Titles"), (SearchFields.Notes, "Notes") })
        {
            var check = new CheckBox { Content = label, IsChecked = true, Name = "Find" + label, Margin = new Thickness(0,0,16,0) };
            AutomationProperties.SetName(check, "Search " + label.ToLowerInvariant()); fields[field] = check; fieldRow.Children.Add(check);
        }
        AutomationProperties.SetName(matchCase, "Match case"); AutomationProperties.SetName(wholeWord, "Whole word");
        var summary = Ui.Text("Enter text to search.", 12, true); summary.Name = "FindSummary"; AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        var list = new ListBox { Name = "FindMatches" }; AutomationProperties.SetName(list, "Text matches. Select a match to preview its replacement.");
        var before = new TextBlock { Name = "FindBefore", TextWrapping = TextWrapping.Wrap, MaxHeight = 72, Inlines = new InlineCollection() };
        var after = new TextBlock { Name = "FindAfter", TextWrapping = TextWrapping.Wrap, MaxHeight = 72, Inlines = new InlineCollection() };
        Window? window = null;
        TextMatch? Current() => (list.SelectedItem as ListBoxItem)?.Tag as TextMatch;
        SearchOptions Options() => new(query.Text ?? "", fields.Where(f => f.Value.IsChecked == true).Aggregate(SearchFields.None, (result, f) => result | f.Key), matchCase.IsChecked == true, wholeWord.IsChecked == true, Equals(scope.SelectedItem, "Selected scene") ? sceneIndex : null);
        var go = Ui.Button("Go to match", () => window!.Close(Current())); go.Name = "FindGo";
        Button replaceOne = null!, replaceAll = null!;
        replaceOne = Ui.Button("Replace selected", () =>
        {
            if (Current() is not { } match) return;
            actions.Mutate(() => ProjectSearch.ReplaceMatch(getProject(), match, replacement.Text ?? "")); Populate("Replaced the selected match. ");
        }); replaceOne.Name = "FindReplaceOne";
        replaceAll = Ui.Button("Replace all", () =>
        {
            var count = 0; actions.Mutate(() => count = ProjectSearch.ReplaceAll(getProject(), Options(), replacement.Text ?? ""));
            Populate($"Replaced {count} {(count == 1 ? "match" : "matches")}. ");
        }, "primary"); replaceAll.Name = "FindReplaceAll";
        void Preview()
        {
            var match = Current(); go.IsEnabled = replaceOne.IsEnabled = match is not null;
            before.Text = after.Text = null;
            before.Inlines!.Clear(); after.Inlines!.Clear();
            if (match is null) { before.Inlines!.Add(new Run("Select a match to see its context.")); return; }
            FillPreview(before, match, match.OriginalText.Substring(match.Start, match.Length)); FillPreview(after, match, replacement.Text ?? "");
        }
        void Populate(string message = "")
        {
            var options = Options(); var results = ProjectSearch.Find(project, options);
            summary.Text = message + (options.Query.Length == 0 ? "Enter text to search." : options.Fields == SearchFields.None ? "Choose at least one field to search." : $"{results.TotalMatches} {(results.TotalMatches == 1 ? "match" : "matches")} in {scope.SelectedItem!.ToString()!.ToLowerInvariant()}.")
                + (results.TotalMatches > results.Matches.Count ? $" Showing the first {results.Matches.Count}; Replace all includes every match in this scope." : "");
            list.ItemsSource = results.Matches.Select(match =>
            {
                var scene = project.ListOfCharacterStates[match.SceneIndex];
                var context = new TextBlock { TextWrapping = TextWrapping.Wrap, Inlines = new InlineCollection() }; FillPreview(context, match, match.OriginalText.Substring(match.Start, match.Length));
                return new ListBoxItem { Tag = match, Content = Ui.Stack(Ui.Text(scene.Title + " [" + match.StateId + "] · " + match.FieldLabel, 12, true), context) };
            }).ToList();
            list.SelectedIndex = results.Matches.Count > 0 ? 0 : -1; replaceAll.IsEnabled = results.TotalMatches > 0; Preview();
        }
        query.TextChanged += (_, _) => Populate(); replacement.TextChanged += (_, _) => Preview();
        scope.SelectionChanged += (_, _) => Populate();
        foreach (var check in fields.Values.Append(matchCase).Append(wholeWord)) check.IsCheckedChanged += (_, _) => Populate();
        list.SelectionChanged += (_, _) => Preview();
        var inputs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") }; inputs.Children.Add(Ui.Field("Find", query)); var replaceField = Ui.Field("Replace with", replacement); Grid.SetColumn(replaceField, 2); inputs.Children.Add(replaceField);
        var optionsRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; optionsRow.Children.Add(Ui.Field("Search scope", scope)); var flags = Ui.Row(matchCase, wholeWord); flags.Margin = new Thickness(20,18,0,0); Grid.SetColumn(flags, 1); optionsRow.Children.Add(flags);
        var header = Ui.Stack(Ui.Text("Find & replace", 25), inputs, optionsRow, fieldRow, summary); header.Margin = new Thickness(0,0,0,12);
        var previews = Ui.Card(Ui.Stack(Ui.Text("MATCH", 11, true), before, Ui.Text("REPLACEMENT PREVIEW", 11, true), after), 12);
        previews.Margin = new Thickness(0,12,0,12);
        var footer = Ui.Stack(Ui.Text("Literal text search includes formatting and condition tags. Each replacement can be undone after closing this window.", 12, true), Ui.Row(Ui.Button("Close", () => window!.Close()), go, replaceOne, replaceAll));
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(24) };
        content.Children.Add(header); Grid.SetRow(list, 1); content.Children.Add(list); Grid.SetRow(previews, 2); content.Children.Add(previews); Grid.SetRow(footer, 3); content.Children.Add(footer);
        window = Ui.Dialog(owner, "Find & replace", content, 780, 740, scale); window.MinWidth = 700 * scale; window.MinHeight = 620 * scale;
        window.Opened += (_, _) => query.Focus(); Populate();
        if (await window.ShowDialog<TextMatch?>(owner) is { } destination) actions.GoToMatch(destination);
    }
    private static void FillPreview(TextBlock block, TextMatch match, string replacement)
    {
        const int context = 45;
        var start = Math.Max(0, match.Start - context); var end = Math.Min(match.OriginalText.Length, match.Start + match.Length + context);
        string Line(string text) => text.Replace("\r", "").Replace("\n", " ↵ ");
        var inlines = block.Inlines ??= new InlineCollection();
        inlines.Add(new Run((start > 0 ? "…" : "") + Line(match.OriginalText[start..match.Start])));
        var shown = replacement.Length > 100 ? replacement[..100] + "…" : replacement;
        inlines.Add(new Run(Line(shown)) { FontWeight = FontWeight.Bold, Foreground = Ui.Brush("MintInk") });
        inlines.Add(new Run(Line(match.OriginalText[(match.Start + match.Length)..end]) + (end < match.OriginalText.Length ? "…" : "")));
    }
}
