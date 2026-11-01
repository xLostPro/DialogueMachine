using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine;
using DialogueMachine.Core;
using DialogueMachine.UI;
using System.Text.Json;
using static UiHelpers;


internal static class SearchChecks
{
    internal static string SearchBefore = "";
    internal static DialogueProject ExpectedSearch = null!, ExpectedAll = null!;

    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var searchStory = ProjectStore.Parse("""
        {"ProjectName":"cat","EntryStateId":"cat","FutureRoot":"cat","ListOfCharacterStates":[
        {"State":"cat","Text":"Cat cat catapult.\n[b]cat[/b] {player_name}","DefaultNextId":"cat2","Metadata":{"title":"Cat crossing","notes":"A cat note.","speaker":"cat","tags":"cat","future":"cat"},"FutureScene":"cat","ShopData":{"ShopId":"cat","FutureShop":"cat"},"DefaultAfterActions":[{"Type":"Event","TargetId":"cat","Parameter":"cat","FutureAction":"cat"}],"Choices":[{"ButtonText":"cat?","NextStateId":"cat2","FutureChoice":"cat","Requirements":[{"TargetId":"cat","Value":"1","FutureRequirement":"cat"}]}]},
        {"State":"cat2","Text":"cat elsewhere.","DefaultNextId":"[EXIT]","Metadata":{"title":"Cat again"}}]}
        """);
        var searchBefore = ProjectStore.Serialize(searchStory);
        var searchResults = ProjectSearch.Find(searchStory, new("cat"));
        Check(searchResults.TotalMatches == 9 && searchResults.Matches.Select(m => m.Field).Distinct().Count() == 4, "project search finds dialogue, responses, titles and notes with occurrence counts");
        Check(searchResults.Matches.All(m => m.OriginalText.Substring(m.Start, m.Length).Equals("cat", StringComparison.OrdinalIgnoreCase)), "search previews point to exact original text offsets across fields");
        Check(ProjectSearch.Find(searchStory, new("cat", MatchCase: true)).TotalMatches == 6 && ProjectSearch.Find(searchStory, new("cat", WholeWord: true)).TotalMatches == 8, "case and whole-word options distinguish capital letters and word fragments");
        Check(ProjectSearch.Find(searchStory, new("cat", SearchFields.Dialogue, SceneIndex: 1)).TotalMatches == 1, "field and scene scope constrain search together");
        Check(ProjectSearch.Find(searchStory, new("cat", SearchFields.None)).TotalMatches == 0 && ProjectSearch.Find(searchStory, new("")).TotalMatches == 0, "empty queries and unchecked fields never match every position");
        Check(ProjectStore.Serialize(searchStory) == searchBefore, "search and previews leave the complete project unchanged");
        var literalStory = ProjectStore.Clone(searchStory);
        Check(ProjectSearch.ReplaceAll(literalStory, new("[b]cat[/b]"), "$1\\literal") == 1 && literalStory.ListOfCharacterStates[0].Text.Contains("$1\\literal"), "find and replacement treat formatting brackets, dollars and backslashes literally");
        var expectedSearch = ProjectStore.Clone(searchStory); expectedSearch.ListOfCharacterStates[0].Text = "Cat dog catapult.\n[b]dog[/b] {player_name}";
        Check(ProjectSearch.ReplaceAll(searchStory, new("cat", SearchFields.Dialogue, MatchCase: true, WholeWord: true, SceneIndex: 0), "dog") == 2 && ProjectStore.Serialize(searchStory) == ProjectStore.Serialize(expectedSearch), "scoped replacement changes only matching occurrences and preserves every other JSON field");
        searchStory = ProjectStore.Parse(searchBefore); var expectedAll = ProjectStore.Clone(searchStory);
        expectedAll.ListOfCharacterStates[0].Metadata["title"] = "dog crossing"; expectedAll.ListOfCharacterStates[0].Metadata["notes"] = "A dog note.";
        expectedAll.ListOfCharacterStates[0].Text = "dog dog catapult.\n[b]dog[/b] {player_name}"; expectedAll.ListOfCharacterStates[0].Choices[0].ButtonText = "dog?";
        expectedAll.ListOfCharacterStates[1].Metadata["title"] = "dog again"; expectedAll.ListOfCharacterStates[1].Text = "dog elsewhere.";
        Check(ProjectSearch.ReplaceAll(searchStory, new("cat", WholeWord: true), "dog") == 8 && ProjectStore.Serialize(searchStory) == ProjectStore.Serialize(expectedAll), "bulk replacement preserves IDs, links, variables, logic, speaker, tags and nested unknown data");
        searchStory = ProjectStore.Parse(searchBefore); var responseMatch = ProjectSearch.Find(searchStory, new("cat", SearchFields.Responses)).Matches.Single();
        ProjectSearch.ReplaceMatch(searchStory, responseMatch, "");
        Check(searchStory.ListOfCharacterStates[0].Choices[0].ButtonText == "?" && searchStory.ListOfCharacterStates[0].Text == "Cat cat catapult.\n[b]cat[/b] {player_name}", "selected replacement can remove text from just one response");
        var afterSingleReplace = ProjectStore.Serialize(searchStory);
        Throws(() => ProjectSearch.ReplaceMatch(searchStory, responseMatch, "again"), "stale match previews cannot overwrite later field edits");
        Check(ProjectStore.Serialize(searchStory) == afterSingleReplace, "a rejected stale replacement does not partially mutate the project");
        var duplicateSearch = Templates.Blank(); duplicateSearch.ListOfCharacterStates[0].Text = "one";
        duplicateSearch.ListOfCharacterStates.Add(new() { State = duplicateSearch.ListOfCharacterStates[0].State, Text = "one" });
        ProjectSearch.ReplaceMatch(duplicateSearch, ProjectSearch.Find(duplicateSearch, new("one", SceneIndex: 1)).Matches.Single(), "two");
        Check(duplicateSearch.ListOfCharacterStates[0].Text == "one" && duplicateSearch.ListOfCharacterStates[1].Text == "two", "match locations distinguish repairable scenes with duplicate IDs");
        var fallbackTitle = Templates.Blank(); fallbackTitle.ListOfCharacterStates[0].Metadata.Remove("title"); var fallbackId = fallbackTitle.ListOfCharacterStates[0].State;
        ProjectSearch.ReplaceAll(fallbackTitle, new(fallbackId, SearchFields.Titles), "Opening");
        Check(fallbackTitle.ListOfCharacterStates[0].Title == "Opening" && fallbackTitle.Entry == fallbackId, "replacing a fallback scene title creates metadata without renaming graph identity");
        var repeatedSearch = Templates.Blank(); repeatedSearch.ListOfCharacterStates[0].Text = string.Join(" ", Enumerable.Repeat("cat", 250));
        var limitedSearch = ProjectSearch.Find(repeatedSearch, new("cat"));
        Check(limitedSearch.TotalMatches == 250 && limitedSearch.Matches.Count == ProjectSearch.PreviewLimit, "preview limits bound UI results while keeping the complete match count");
        Check(ProjectSearch.ReplaceAll(repeatedSearch, new("cat"), "catcat") == 250 && repeatedSearch.ListOfCharacterStates[0].Text.Split(' ').All(t => t == "catcat"), "Replace all includes results beyond the preview limit without searching inserted text again");
        repeatedSearch.ListOfCharacterStates[0].Text = "aaa";
        Check(ProjectSearch.ReplaceAll(repeatedSearch, new("aa"), "X") == 1 && repeatedSearch.ListOfCharacterStates[0].Text == "Xa", "overlapping literals use predictable nonoverlapping replacement");
        repeatedSearch.ListOfCharacterStates[0].Text = "cat catapult cat_1 1cat cat-cat cát cat\u0301";
        Check(ProjectSearch.Find(repeatedSearch, new("cat", WholeWord: true)).TotalMatches == 3, "whole-word boundaries include digits, underscores and combining accents");
        var unchangedSearch = ProjectStore.Serialize(repeatedSearch);
        Check(ProjectSearch.ReplaceAll(repeatedSearch, new("cat"), "cat") == 7 && ProjectStore.Serialize(repeatedSearch) == unchangedSearch, "identical replacements leave serialized data unchanged");
        SearchBefore = searchBefore; ExpectedSearch = expectedSearch; ExpectedAll = expectedAll;
    }

    public static void UI(Action<bool, string> Check, Action<Window, string> Capture, string workflowRoot)
    {
        var searchBefore = SearchBefore; var expectedSearch = ExpectedSearch; var expectedAll = ExpectedAll;
        var findFile = Path.Combine(workflowRoot, "find-story.dialogue.json"); ProjectStore.AtomicWrite(findFile, searchBefore);
        var findWorkspace = new MainWindow(null, true); findWorkspace.Show(); Pump(findWorkspace.OpenProjectAsync(findFile));
        Window OpenFind()
        {
            Press(findWorkspace.FindControl<Button>("FindReplaceButton")!); return Modal(findWorkspace);
        }
        void FindUndo() => Press(findWorkspace.FindControl<Button>("UndoButton")!);
        string InlineText(Window owner, string name) => string.Concat(NamedControl<TextBlock>(owner, name).Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text));
        var finder = OpenFind();
        Check(!NamedControl<Button>(finder, "FindReplaceAll").IsEnabled && NamedControl<TextBox>(finder, "FindQuery").IsFocused, "find opens with keyboard focus and disables empty-query replacements");
        NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "dog"; Dispatcher.UIThread.RunJobs();
        Check(NamedControl<ListBox>(finder, "FindMatches").Items.Count == 9 && InlineText(finder, "FindBefore") == "Cat crossing" && InlineText(finder, "FindAfter") == "dog crossing", "find dialog lists occurrences and previews the selected replacement in context");
        Check(ProjectStore.Serialize(findWorkspace.Project) == searchBefore && !findWorkspace.HasUnsavedChanges, "changing query, replacement and previews does not dirty the project");
        Check(finder.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or ComboBox or CheckBox).All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "find inputs, scope and checkboxes have accessibility labels");
        Capture(finder, "find-dark");
        NamedControl<CheckBox>(finder, "FindWholeWord").IsChecked = true; NamedControl<ComboBox>(finder, "FindScope").SelectedItem = "Selected scene";
        NamedControl<CheckBox>(finder, "FindTitles").IsChecked = false; NamedControl<CheckBox>(finder, "FindResponses").IsChecked = false; NamedControl<CheckBox>(finder, "FindNotes").IsChecked = false; Dispatcher.UIThread.RunJobs();
        Check(NamedControl<ListBox>(finder, "FindMatches").Items.Count == 3 && NamedControl<TextBlock>(finder, "FindSummary").Text!.Contains("selected scene"), "dialog field and scene scope refresh the match list");
        NamedControl<CheckBox>(finder, "FindMatchCase").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Check(NamedControl<ListBox>(finder, "FindMatches").Items.Count == 2, "dialog case matching updates scoped occurrences");
        PressAnswer(finder, "Replace all");
        Check(ProjectStore.Serialize(findWorkspace.Project) == ProjectStore.Serialize(expectedSearch) && findWorkspace.HasUnsavedChanges && NamedControl<TextBlock>(finder, "FindSummary").Text!.StartsWith("Replaced 2 matches."), "actual Replace all applies precisely the previewed scope and reports its count");
        PressAnswer(finder, "Close"); FindUndo();
        Check(ProjectStore.Serialize(findWorkspace.Project) == searchBefore && !findWorkspace.HasUnsavedChanges, "one Undo restores an entire scoped replacement and clears dirty state");
        Press(findWorkspace.FindControl<Button>("RedoButton")!);
        Check(ProjectStore.Serialize(findWorkspace.Project) == ProjectStore.Serialize(expectedSearch), "Redo reapplies the complete replacement"); FindUndo();
        finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "dog"; NamedControl<CheckBox>(finder, "FindWholeWord").IsChecked = true;
        Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Replace all"); PressAnswer(finder, "Close");
        Check(ProjectStore.Serialize(findWorkspace.Project) == ProjectStore.Serialize(expectedAll), "project-wide UI replacement updates all four fields across scenes without losing unknown data"); FindUndo();
        finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "";
        NamedControl<CheckBox>(finder, "FindTitles").IsChecked = false; NamedControl<CheckBox>(finder, "FindDialogue").IsChecked = false; NamedControl<CheckBox>(finder, "FindNotes").IsChecked = false; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Replace selected");
        Check(findWorkspace.Project.ListOfCharacterStates[0].Choices[0].ButtonText == "?" && NamedControl<ListBox>(finder, "FindMatches").Items.Count == 0 && !NamedControl<Button>(finder, "FindReplaceOne").IsEnabled, "Replace selected deletes one response occurrence and refreshes the remaining matches");
        PressAnswer(finder, "Close"); FindUndo();
        finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "[b]cat[/b]"; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Go to match");
        var foundInput = findWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Dialogue text");
        Check(foundInput.IsFocused && foundInput.SelectedText == "[b]cat[/b]", "Go to match focuses and selects the exact dialogue occurrence");
        findWorkspace.FindControl<TextBox>("SearchBox")!.Text = "no outline match";
        finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "note"; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Go to match");
        foundInput = findWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Writer's notes");
        Check(foundInput.IsFocused && foundInput.SelectedText == "note" && findWorkspace.FindControl<TextBox>("SearchBox")!.Text == "", "Go to match opens Notes and clears an outline filter that hides the scene");
        finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat?"; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Go to match");
        foundInput = findWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Player response");
        Check(foundInput.IsFocused && foundInput.SelectedText == "cat?", "Go to match selects the matching response editor");
        finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat";
        foreach (var field in new[] { "FindDialogue", "FindResponses", "FindTitles", "FindNotes" }) NamedControl<CheckBox>(finder, field).IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Check(!NamedControl<Button>(finder, "FindReplaceAll").IsEnabled && NamedControl<TextBlock>(finder, "FindSummary").Text!.Contains("Choose at least one field"), "unchecking every field explains the empty scope and disables replacement");
        finder.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs();
        Check(!finder.IsVisible && ProjectStore.Serialize(findWorkspace.Project) == searchBefore, "Escape closes search without applying a preview");
        findWorkspace.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h"); findWorkspace.KeyRelease(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h"); Dispatcher.UIThread.RunJobs();
        finder = Modal(findWorkspace); Check(NamedControl<TextBox>(finder, "FindQuery").IsFocused, "Ctrl+H opens find and replace from the keyboard"); PressAnswer(finder, "Close");
        findWorkspace.KeyPress(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F, "F"); findWorkspace.KeyRelease(Key.F, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F, "F"); Dispatcher.UIThread.RunJobs();
        finder = Modal(findWorkspace); Check(NamedControl<TextBox>(finder, "FindQuery").IsFocused, "Ctrl+Shift+F opens project-wide find without changing outline search"); PressAnswer(finder, "Close");
        foreach (var theme in new[] { "Light", "High contrast" })
        {
            findWorkspace.ChangeAppearance(theme); finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "dog"; Dispatcher.UIThread.RunJobs();
            Capture(finder, "find-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(finder, "Close");
        }
        foreach (var scale in new[] { 1.15, 1.3 })
        {
            findWorkspace.ChangeAppearance("Dark", scale); finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; Dispatcher.UIThread.RunJobs();
            Check(finder.Content is LayoutTransformControl && Math.Abs(finder.Width - 780 * scale) < 1 && NamedControl<Button>(finder, "FindReplaceAll").IsEffectivelyVisible, "find dialog scales its content and window at " + scale);
            Capture(finder, "find-scale-" + (int)(scale * 100)); PressAnswer(finder, "Close");
        }
        findWorkspace.ChangeAppearance("Dark"); finder = OpenFind(); finder.Width = finder.MinWidth; finder.Height = finder.MinHeight;
        NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; Dispatcher.UIThread.RunJobs(); Capture(finder, "find-compact");
        var findFooterButton = NamedControl<Button>(finder, "FindReplaceAll"); var footerPoint = findFooterButton.TranslatePoint(default, finder)!.Value;
        Check(footerPoint.Y >= 0 && footerPoint.Y + findFooterButton.Bounds.Height <= finder.ClientSize.Height && footerPoint.X + findFooterButton.Bounds.Width <= finder.ClientSize.Width, "compact find dialog keeps replacement controls within the window");
        PressAnswer(finder, "Close");
        var pendingTitle = findWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Scene title");
        pendingTitle.Focus(); pendingTitle.Text = "Pending cat edit"; Dispatcher.UIThread.RunJobs();
        finder = OpenFind(); NamedControl<TextBox>(finder, "FindQuery").Text = "cat"; NamedControl<TextBox>(finder, "FindReplacement").Text = "dog";
        NamedControl<CheckBox>(finder, "FindDialogue").IsChecked = false; NamedControl<CheckBox>(finder, "FindResponses").IsChecked = false; NamedControl<CheckBox>(finder, "FindNotes").IsChecked = false;
        NamedControl<ComboBox>(finder, "FindScope").SelectedItem = "Selected scene"; Dispatcher.UIThread.RunJobs(); PressAnswer(finder, "Replace all"); PressAnswer(finder, "Close"); FindUndo();
        Check(findWorkspace.Project.ListOfCharacterStates[0].Title == "Pending cat edit", "replacement Undo preserves typing committed immediately before opening find");
        FindUndo(); Check(ProjectStore.Serialize(findWorkspace.Project) == searchBefore && !findWorkspace.HasUnsavedChanges, "typing before find retains its separate undo entry");

        var locExportPath = Path.Combine(workflowRoot, "exported-translations.csv");
        Pump(findWorkspace.ExportLocalizationAsync(locExportPath));
        Check(File.Exists(locExportPath) && File.ReadAllText(locExportPath).Contains("cat2:dialogue"), "ExportLocalizationAsync exports project translation table to disk");

        var locEdit = File.ReadAllText(locExportPath).Replace("Cat crossing", "Chat traversant");
        File.WriteAllText(locExportPath, locEdit);
        var beforeImport = ProjectStore.Serialize(findWorkspace.Project);
        Pump(findWorkspace.ImportLocalizationAsync(locExportPath));
        Check(findWorkspace.Project.ListOfCharacterStates[0].Title == "Chat traversant", "ImportLocalizationAsync updates scene title from CSV file");
        FindUndo();
        Check(ProjectStore.Serialize(findWorkspace.Project) == beforeImport, "Imported translation changes undo in one step");

        findWorkspace.Close();
    }
}
