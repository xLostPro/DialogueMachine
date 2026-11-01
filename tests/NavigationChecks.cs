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


internal static class NavigationChecks
{
    internal static DialogueProject Story = null!;

    public static void Core(Action<bool, string> Check, Action<Action, string> Throws)
    {
        var navigationStory = Templates.Harbour(); navigationStory.ListOfCharacterStates[0].DefaultNextId = "keeper"; navigationStory.ListOfCharacterStates[0].Choices[0].NextStateId = null;
        navigationStory.ListOfCharacterStates[0].Metadata["tags"] = "coast, Mystery, mystery, ,";
        navigationStory.ListOfCharacterStates[1].Metadata["speaker"] = "Ada"; navigationStory.ListOfCharacterStates[1].Metadata["tags"] = "Mystery";
        navigationStory.ListOfCharacterStates[2].Metadata["tags"] = "coast";
        navigationStory.ListOfCharacterStates[3].Metadata["speaker"] = "Ada"; navigationStory.ListOfCharacterStates[3].Metadata["tags"] = "ending";
        navigationStory.ListOfCharacterStates[4].Metadata["tags"] = "ending";
        var navigationBefore = ProjectStore.Serialize(navigationStory);
        var navigationIndex = new SceneNavigation(navigationStory, [new("keeper", "Error", "Example error"), new("gift", "Warning", "Example warning")]);
        Check(navigationIndex.Tags.SequenceEqual(new[] { "coast", "ending", "Mystery" }) && navigationIndex.Speakers.SequenceEqual(new[] { "Ada", "Mara" }), "navigation discovers sorted speakers and trims/deduplicates comma-separated tags");
        Check(navigationIndex.Filter(new(Speaker: "mara", Tag: "mystery")).Select(s => s.Id).SequenceEqual(new[] { "arrival" }), "outline filters combine exact speaker and tag matches without case sensitivity");
        Check(navigationIndex.Filter(new("keeper", Speaker: "Ada", Tag: "Mystery", Issues: SceneIssueFilter.Errors)).Single().Id == "keeper", "text, speaker, tag and diagnostic filters intersect");
        Check(navigationIndex.Filter(new(Issues: SceneIssueFilter.WithIssues)).Count == 2 && navigationIndex.Filter(new(Issues: SceneIssueFilter.Warnings)).Single().Id == "gift" && navigationIndex.Filter(new(Issues: SceneIssueFilter.WithoutIssues)).Count == 3, "diagnostic filters distinguish errors, warnings, any issues and clean scenes");
        Check(navigationIndex.Filter(new("coast")).Count == 2 && navigationIndex.Filter(new("Establish the mystery")).Single().Id == "arrival", "outline text search includes tags and writer's notes");
        Check(navigationIndex.Outgoing(0).Count == 4 && navigationIndex.Outgoing(0)[1].TargetId == "keeper" && navigationIndex.Outgoing(0)[1].TargetSceneIndex == 1, "outgoing choice links resolve the same continue fallback as the runtime");
        Check(navigationIndex.Incoming(1).Select(s => s.Id).SequenceEqual(new[] { "arrival", "gift" }), "incoming navigation deduplicates source scenes with multiple links");
        Check(navigationIndex.Resolve("[EXIT]") is null && navigationIndex.Resolve("missing") is null && navigationIndex.Resolve("KEEPER") is null, "navigation preserves case-sensitive identity and distinguishes endings and missing targets");
        var destinationOptions = navigationIndex.Destinations(true, "missing");
        Check(destinationOptions[0].Id is null && destinationOptions[0].Label.Contains("continue") && destinationOptions[1].Id == "[EXIT]" && destinationOptions[^1].Id == "missing", "destination options preserve fallback, ending and a current missing target");
        Check(SceneNavigation.FindDestinations(destinationOptions, "SMALL KINDNESS").Matches.Single().Id == "gift" && SceneNavigation.FindDestinations(destinationOptions, "promise").Matches.Single().Id == "promise", "destination search matches both displayed titles and exact IDs case-insensitively");
        Check(ProjectStore.Serialize(navigationStory) == navigationBefore, "indexing, filtering and connection navigation never modify story JSON");
        var nullNavigation = ProjectStore.Parse("""{"ListOfCharacterStates":[{"State":"start","Text":"Hello","Metadata":{"title":null,"speaker":null,"notes":null,"tags":null}}]}"""); var nullNavigationBefore = ProjectStore.Serialize(nullNavigation);
        Check(new SceneNavigation(nullNavigation, []).Filter(new(Speaker: "Character")).Single().Title == "start" && ProjectStore.Serialize(nullNavigation) == nullNavigationBefore, "null imported metadata remains navigable without rewriting legacy JSON");
        foreach (var size in new[] { 100, 1000, 10000 })
        {
            var largeNavigation = new DialogueProject { ListOfCharacterStates = Enumerable.Range(0, size).Select(i => Templates.Scene($"scene_{i:D5}", "Chapter " + i, i % 2 == 0 ? "Ada" : "Mara", "A line of dialogue.", 0, 0)).ToList() };
            var largeIndex = new SceneNavigation(largeNavigation, []);
            var largeMatches = SceneNavigation.FindDestinations(largeIndex.Destinations(false, null), "Chapter");
            var filteredNavigation = largeIndex.Filter(new($"scene_{size - 1:D5}", Speaker: "Mara"));
            Check(largeMatches.TotalCount == size && largeMatches.Matches.Count == Math.Min(size, SceneNavigation.DestinationLimit) && filteredNavigation.Single().Index == size - 1, "large navigation retains complete counts with bounded destination previews at " + size + " scenes");
        }
        Story = navigationStory;
    }

    public static void UI(Action<bool, string> Check, Action<Window, string> Capture, string workflowRoot)
    {
        var navigationStory = Story;
        var navigationUiStory = ProjectStore.Clone(navigationStory); navigationUiStory.ListOfCharacterStates[2].Text += " {unknown_text}"; navigationUiStory.ListOfCharacterStates[3].DefaultNextId = "missing_target";
        navigationUiStory.Extra = new() { ["FutureNavigation"] = JsonSerializer.SerializeToElement(new { keep = "keeper" }) };
        var navigationUiBefore = ProjectStore.Serialize(navigationUiStory); var navigationFile = Path.Combine(workflowRoot, "navigation.dialogue.json"); ProjectStore.AtomicWrite(navigationFile, navigationUiBefore);
        var navigationWorkspace = new MainWindow(null, true); navigationWorkspace.Show(); Pump(navigationWorkspace.OpenProjectAsync(navigationFile));
        void NavigationUndo() => Press(navigationWorkspace.FindControl<Button>("UndoButton")!);
        void OutlineOption(string name, string label)
        {
            var combo = navigationWorkspace.FindControl<ComboBox>(name)!; combo.SelectedItem = combo.Items.Cast<object>().Single(o => o.ToString() == label); Dispatcher.UIThread.RunJobs();
        }
        void NavigationScene(string id)
        {
            Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!); var scenes = navigationWorkspace.FindControl<ListBox>("SceneList")!;
            scenes.SelectedItem = scenes.Items.Cast<NavigationScene>().Single(item => item.Id == id); Dispatcher.UIThread.RunJobs();
        }
        Window OpenDestination(string name) { Press(NamedButton(navigationWorkspace, name)); return Modal(navigationWorkspace); }
        var destinationPicker = OpenDestination("Choose choice 1 destination");
        Check(NamedControl<TextBox>(destinationPicker, "DestinationSearch").IsFocused && NamedControl<ListBox>(destinationPicker, "DestinationMatches").Items.Cast<DestinationOption>().First().Id is null, "destination picker focuses search and preserves the inherited continue option");
        NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "SMALL KINDNESS"; Dispatcher.UIThread.RunJobs();
        Check(NamedControl<ListBox>(destinationPicker, "DestinationMatches").Items.Cast<DestinationOption>().Single().Id == "gift" && ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore, "typing a destination title narrows results without editing the story");
        PressAnswer(destinationPicker, "Cancel"); Check(ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore, "Cancel leaves destination, unknown fields and history unchanged");
        destinationPicker = OpenDestination("Choose choice 1 destination"); NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "gift"; Dispatcher.UIThread.RunJobs();
        destinationPicker.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); destinationPicker.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, ""); Dispatcher.UIThread.RunJobs();
        Check(NamedControl<ListBox>(destinationPicker, "DestinationMatches").IsKeyboardFocusWithin, "Down moves from destination search into keyboard results");
        destinationPicker.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Dispatcher.UIThread.RunJobs();
        var expectedDestination = ProjectStore.Parse(navigationUiBefore); expectedDestination.ListOfCharacterStates[0].Choices[0].NextStateId = "gift";
        Check(!destinationPicker.IsVisible && ProjectStore.Serialize(navigationWorkspace.Project) == ProjectStore.Serialize(expectedDestination), "Enter chooses the selected destination and changes only that connection");
        NavigationUndo(); Check(ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore && !navigationWorkspace.HasUnsavedChanges, "one Undo restores the previous fallback and all unknown data");
        destinationPicker = OpenDestination("Choose continue destination"); NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "no continue"; Dispatcher.UIThread.RunJobs(); PressAnswer(destinationPicker, "Choose destination");
        Check(navigationWorkspace.Project.ListOfCharacterStates[0].DefaultNextId is null && navigationWorkspace.Project.ListOfCharacterStates[0].Choices[0].NextStateId is null, "choosing no continue keeps an inherited choice unset");
        navigationWorkspace.FindControl<ComboBox>("IssueFilter")!.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
        Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Cast<NavigationScene>().Any(item => item.Id == "arrival"), "diagnostic filters refresh immediately after changing a connection");
        NavigationUndo(); Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!);
        destinationPicker = OpenDestination("Choose choice 1 destination"); NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "nothing matches this"; Dispatcher.UIThread.RunJobs();
        Check(!NamedControl<Button>(destinationPicker, "ChooseDestination").IsEnabled && NamedControl<TextBlock>(destinationPicker, "DestinationSummary").Text!.StartsWith("No matching"), "empty destination results explain the state and disable Choose");
        destinationPicker.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, ""); Dispatcher.UIThread.RunJobs(); Check(!destinationPicker.IsVisible && !navigationWorkspace.HasUnsavedChanges, "Escape cancels destination search without creating history");
        navigationWorkspace.FindControl<Expander>("OutlineFilters")!.IsExpanded = true; OutlineOption("SpeakerFilter", "Ada"); OutlineOption("TagFilter", "Mystery");
        navigationWorkspace.FindControl<TextBox>("SearchBox")!.Text = "keeper"; Dispatcher.UIThread.RunJobs();
        Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Cast<NavigationScene>().Single() is NavigationScene { Id: "keeper" } && navigationWorkspace.FindControl<TextBlock>("SceneCount")!.Text == "1/5", "outline combines speaker, tag and text filters and reports visible/total scenes");
        Capture(navigationWorkspace, "navigation-filters-dark"); navigationWorkspace.FindControl<TextBox>("SearchBox")!.Text = "absent scene"; Dispatcher.UIThread.RunJobs();
        Check(navigationWorkspace.FindControl<TextBlock>("OutlineEmpty")!.IsVisible && navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Count == 0, "an empty filtered outline offers clear-search guidance");
        Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!); navigationWorkspace.FindControl<ComboBox>("IssueFilter")!.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
        Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Cast<NavigationScene>().Single() is NavigationScene { Id: "promise" }, "the Errors outline filter lists the actual broken-target scene");
        navigationWorkspace.FindControl<ComboBox>("IssueFilter")!.SelectedIndex = 3; Dispatcher.UIThread.RunJobs();
        Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Cast<NavigationScene>().Any(item => item.Id == "gift"), "the Warnings outline filter includes unknown substitution diagnostics");
        Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!);
        Check(navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Count == 5 && ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore && !navigationWorkspace.HasUnsavedChanges, "clearing filters restores the full outline without editing or dirtying the story");
        OutlineOption("SpeakerFilter", "Mara"); OutlineOption("TagFilter", "coast"); Press(NamedButton(navigationWorkspace, "Jump to choice 1 destination"));
        Check(navigationWorkspace.FindControl<TextBlock>("EditorSubtitle")!.Text!.StartsWith("keeper") && navigationWorkspace.FindControl<ListBox>("SceneList")!.Items.Count == 5, "Jump follows the inherited destination and reveals a scene hidden by filters");
        Press(navigationWorkspace.FindControl<Button>("NotesTab")!);
        Check(NamedButton(navigationWorkspace, "← A light in the fog [arrival]").IsEnabled && NamedButton(navigationWorkspace, "← A small kindness [gift]").IsEnabled, "Notes presents both incoming source scenes with keyboard buttons");
        Press(NamedButton(navigationWorkspace, "Choice 1 → Into the unknown [promise]"));
        Check(navigationWorkspace.FindControl<TextBlock>("EditorSubtitle")!.Text!.StartsWith("promise") && navigationWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Continue → Missing: missing_target"), "outgoing navigation opens its target and explains missing downstream connections");
        Press(navigationWorkspace.FindControl<Button>("ContentTab")!); Check(!NamedButton(navigationWorkspace, "Jump to continue destination").IsEnabled, "missing destination keeps Jump disabled while remaining repairable");
        destinationPicker = OpenDestination("Choose continue destination");
        Check((NamedControl<ListBox>(destinationPicker, "DestinationMatches").SelectedItem as DestinationOption)?.Id == "missing_target", "picker preserves a missing current destination until an explicit choice");
        NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "end conversation"; Dispatcher.UIThread.RunJobs(); PressAnswer(destinationPicker, "Choose destination");
        Check(navigationWorkspace.Project.ListOfCharacterStates[3].DefaultNextId == "[EXIT]" && !NamedButton(navigationWorkspace, "Jump to continue destination").IsEnabled, "choosing an ending writes the reserved ending target without offering scene navigation"); NavigationUndo();
        NavigationScene("arrival"); Press(navigationWorkspace.FindControl<Button>("NotesTab")!);
        var tagInput = navigationWorkspace.FindControl<StackPanel>("EditorPanel")!.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Tags (comma separated)");
        tagInput.Focus(); tagInput.Text += ", newtag"; Dispatcher.UIThread.RunJobs(); navigationWorkspace.FindControl<TextBox>("SearchBox")!.Focus(); Dispatcher.UIThread.RunJobs();
        Check(navigationWorkspace.FindControl<ComboBox>("TagFilter")!.Items.Cast<object>().Any(o => o.ToString() == "newtag"), "committed tag edits refresh available outline filters"); NavigationUndo();
        Check(!navigationWorkspace.FindControl<ComboBox>("TagFilter")!.Items.Cast<object>().Any(o => o.ToString() == "newtag") && ProjectStore.Serialize(navigationWorkspace.Project) == navigationUiBefore, "undo refreshes the navigation index and removes obsolete tags");
        Press(navigationWorkspace.FindControl<Button>("ContentTab")!); destinationPicker = OpenDestination("Choose continue destination");
        Check(destinationPicker.GetVisualDescendants().Where(c => c.TemplatedParent is null && c is TextBox or ListBox).All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "destination search and result list have accessibility labels"); Capture(destinationPicker, "destination-dark"); PressAnswer(destinationPicker, "Cancel");
        foreach (var theme in new[] { "Light", "High contrast" })
        {
            navigationWorkspace.ChangeAppearance(theme); OutlineOption("SpeakerFilter", "Ada"); Capture(navigationWorkspace, "navigation-filters-" + theme.ToLowerInvariant().Replace(' ', '-')); Press(navigationWorkspace.FindControl<Button>("ClearFiltersButton")!);
            destinationPicker = OpenDestination("Choose continue destination"); NamedControl<TextBox>(destinationPicker, "DestinationSearch").Text = "keeper"; Dispatcher.UIThread.RunJobs(); Capture(destinationPicker, "destination-" + theme.ToLowerInvariant().Replace(' ', '-')); PressAnswer(destinationPicker, "Cancel");
        }
        foreach (var scale in new[] { 1.15, 1.3 })
        {
            navigationWorkspace.ChangeAppearance("Dark", scale); destinationPicker = OpenDestination("Choose continue destination");
            Check(destinationPicker.Content is LayoutTransformControl && Math.Abs(destinationPicker.Width - 610 * scale) < 1, "destination picker respects interface scale " + scale); Capture(destinationPicker, "destination-scale-" + (int)(scale * 100)); PressAnswer(destinationPicker, "Cancel");
        }
        navigationWorkspace.ChangeAppearance("Dark"); navigationWorkspace.Width = 1000; navigationWorkspace.Height = 680;
        OutlineOption("SpeakerFilter", "Ada"); Dispatcher.UIThread.RunJobs(); Capture(navigationWorkspace, "navigation-filters-compact");
        Check(navigationWorkspace.FindControl<ComboBox>("IssueFilter")!.IsEffectivelyVisible && navigationWorkspace.FindControl<ListBox>("SceneList")!.Bounds.Height > 30, "compact workspace keeps expanded filters and a scrollable outline available");
        navigationWorkspace.Close();
    }
}
