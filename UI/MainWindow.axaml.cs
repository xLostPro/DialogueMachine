using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DialogueMachine.Core;
using System.IO;

namespace DialogueMachine.UI;

public sealed partial class MainWindow : Window
{
    private DialogueProject project = new();
    private DialogueState? selected;
    private readonly HashSet<DialogueState> selectedScenes = [];
    private readonly ProjectHistory history = new();
    private string savedSnapshot = "";
    private string? filePath;
    private ProjectFileVersion? diskVersion;
    private bool saveInProgress;
    private bool refreshing, allowClose, closePending, hasProject;
    private bool pendingEdits;
    private int wordCount, choiceCount;
    private readonly Dictionary<DialogueState, int> sceneWords = [];
    private readonly DispatcherTimer editTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly string dataDir;
    private string recoveryId = Guid.NewGuid().ToString("N");
    private WorkspacePreferences preferences = new();
    private readonly IProjectFilePicker projectFilePicker;
    private readonly bool persistWorkspace;
    private bool startupInitialised;
    public bool IsTestMode { get; }
    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    private StackPanel Editor => C<StackPanel>("EditorPanel");
    private bool Dirty => hasProject && ProjectStore.Serialize(project) != savedSnapshot;
    public DialogueProject Project => project;
    public bool HasProject => hasProject;
    public bool HasUnsavedChanges => Dirty;
    public string? CurrentFilePath => filePath;
    public MainWindow() : this(null, false) { }
    public MainWindow(string[]? args, bool testMode = false, string? dataDirectory = null, IProjectFilePicker? filePicker = null)
    {
        IsTestMode = testMode || args?.Contains("--smoke-test") == true;
        dataDir = dataDirectory ?? (IsTestMode ? Path.Combine(AppContext.BaseDirectory, "test-data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DialogueMachine"));
        persistWorkspace = !IsTestMode || dataDirectory is not null;
        projectFilePicker = filePicker ?? new NativeProjectFilePicker();
        InitializeComponent(); WireSceneEditor();
        ReadPreferences();
        history.Reset(project); savedSnapshot = ProjectStore.Serialize(project);
        WireActions(); WireWelcome(); WireLiveLink(); BuildMenu(); Refresh(); RefreshWelcome();
        editTimer.Tick += (_, _) => { editTimer.Stop(); Commit(); };
        recoveryTimer.Tick += (_, _) => WriteRecovery(); if (!IsTestMode) recoveryTimer.Start();
        if (args?.Contains("--smoke-test") == true) { WindowStartupLocation = WindowStartupLocation.Manual; Position = new PixelPoint(-5000, -5000); ShowInTaskbar = false; }
        Opened += async (_, _) =>
        {
            Graph.Fit();
            if (args?.Contains("--smoke-test") == true) { await NativeSmoke(args); return; }
            if (!IsTestMode) await InitialiseWorkspaceAsync(args?.FirstOrDefault(a => !a.StartsWith('-')));
        };
        Closing += async (_, e) =>
        {
            if (allowClose || IsTestMode) return;
            e.Cancel = true; if (closePending) return; closePending = true;
            if (await ConfirmReplace()) { allowClose = true; recoveryTimer.Stop(); editTimer.Stop(); Close(); }
            closePending = false;
        };
    }
    private void WireActions()
    {
        void Click(string name, Action action) => C<Button>(name).Click += (_, _) => action();
        void AsyncClick(string name, Func<Task> action) => C<Button>(name).Click += async (_, _) => await action();
        Click("UndoButton", Undo); Click("RedoButton", Redo); AsyncClick("SaveButton", () => Save()); AsyncClick("PlayButton", () => Play());
        AsyncClick("HomeButton", () => ReturnHomeAsync());
        AsyncClick("AddButton", AddScene); AsyncClick("VariablesButton", ShowVariables); AsyncClick("FindReplaceButton", ShowFindReplace); Click("ProblemsButton", ToggleProblems);
        AsyncClick("GroupsButton", ShowGroups); Click("HideMinimap", () => { Graph.Focus(); ToggleMinimap(); }); C<GraphMinimap>("Minimap").Graph = Graph;
        Click("ContentTab", () => SwitchTab("Content")); Click("LogicTab", () => SwitchTab("Logic")); Click("NotesTab", () => SwitchTab("Notes"));
        Click("GraphButton", () => SetView(false)); Click("ScriptButton", () => SetView(true));
        Click("LayoutButton", () => { Mutate(() => DialogueEngine.AutoLayout(project)); Graph.Fit(); Status("Scenes arranged by their connections."); });
        Click("ZoomIn", () => Graph.SetZoom(Graph.Zoom * 1.2)); Click("ZoomOut", () => Graph.SetZoom(Graph.Zoom / 1.2)); Click("FitButton", Graph.Fit);
        Click("CloseDiagnostics", () => C<Border>("DiagnosticsBorder").IsVisible = false);
        C<TextBox>("SearchBox").TextChanged += (_, _) => { if (!updatingOutlineFilters) RefreshOutline(); };
        foreach (var name in new[] { "SpeakerFilter", "TagFilter", "IssueFilter", "GroupFilter" }) C<ComboBox>(name).SelectionChanged += (_, _) => { if (!updatingOutlineFilters) RefreshOutline(); };
        Click("ClearFiltersButton", ClearOutlineFilters);
        C<Grid>("WorkspacePanel").SizeChanged += (_, e) => C<ScrollViewer>("OutlineHeaderScroll").MaxHeight = Math.Max(175, e.NewSize.Height - 365);
        C<ListBox>("SceneList").SelectionChanged += (_, e) =>
        {
            if (refreshing) return;
            var scenes = C<ListBox>("SceneList").SelectedItems!.Cast<NavigationScene>().Select(i => project.ListOfCharacterStates[i.Index]).ToList();
            var added = e.AddedItems.OfType<NavigationScene>().LastOrDefault();
            var active = added is null ? null : project.ListOfCharacterStates[added.Index];
            SelectScenes(scenes, active ?? selected, true);
        };
        Graph.SelectionChanged += () => SelectScenes(Graph.SelectedScenes, Graph.Selected, false);
        Graph.InteractionStarted += Commit;
        Graph.Changed += () => { pendingEdits = true; Commit(false); Refresh(); };
        Graph.ZoomChanged += z => C<TextBlock>("ZoomLabel").Text = $"{z:P0}";
        Graph.SizeChanged += (_, _) =>
        {
            UpdateGraphOverlays();
        };
        C<ListBox>("DiagnosticsList").SelectionChanged += (_, _) =>
        {
            if (C<ListBox>("DiagnosticsList").SelectedItem is ListBoxItem item && item.Tag is Diagnostic d && project.ListOfCharacterStates.FirstOrDefault(s => s.State == d.StateId) is { } s) SelectScene(s, true);
        };
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
    }
    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (command)
        {
            if (!hasProject && e.Key is not (Key.N or Key.O or Key.S)) return;
            switch (e.Key)
            {
                case Key.S: _ = Save(shift); break;
                case Key.O: _ = Open(); break;
                case Key.N: _ = NewProject(); break;
                case Key.F: if (shift) _ = ShowFindReplace(); else { C<TextBox>("SearchBox").Focus(); C<TextBox>("SearchBox").SelectAll(); } break;
                case Key.H: _ = ShowFindReplace(); break;
                case Key.G when shift: _ = ShowGroups(); break;
                case Key.M when shift: ToggleMinimap(); break;
                case Key.Z: if (shift) Redo(); else Undo(); break;
                case Key.Y: Redo(); break;
                case Key.D: DuplicateScene(); break;
                case Key.C when Graph.IsFocused || C<ListBox>("SceneList").IsKeyboardFocusWithin: CopySelectedScenes(); break;
                case Key.V when Graph.IsFocused || C<ListBox>("SceneList").IsKeyboardFocusWithin: _ = PasteScenes(); break;
                case Key.D0: Graph.Fit(); break;
                case Key.OemPlus: Graph.SetZoom(Graph.Zoom * 1.2); break;
                case Key.OemMinus: Graph.SetZoom(Graph.Zoom / 1.2); break;
                default: return;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && hasProject) { _ = Play(shift); e.Handled = true; }
        else if (e.Key == Key.F1) { _ = ShowHelp(); e.Handled = true; }
        else if (e.Key == Key.Delete && e.Source is not TextBox && (Graph.IsFocused || C<ListBox>("SceneList").IsKeyboardFocusWithin)) { _ = DeleteScene(); e.Handled = true; }
    }
    private void BuildMenu()
    {
        MenuItem Item(string name, Action action, string? shortcut = null)
        {
            var m = new MenuItem { Header = name }; if (shortcut is not null) m.InputGesture = KeyGesture.Parse(shortcut);
            m.Click += (_, _) => action(); return m;
        }
        var file = new MenuItem { Header = "_File" };
        var recent = new MenuItem { Header = "Open _recent" };
        foreach (var r in preferences.RecentProjects) { var p = r.Path; var item = Item(r.Name, () => _ = OpenPath(p)); item.IsEnabled = File.Exists(p); recent.Items.Add(item); }
        recent.IsEnabled = preferences.RecentProjects.Count > 0;
        MenuItem ProjectItem(string label, Action action, string? shortcut = null) { var item = Item(label, action, shortcut); item.IsEnabled = hasProject; return item; }
        file.ItemsSource = new object[] { Item("_Home", () => _ = ReturnHomeAsync()), Item("_New project…", () => _ = NewProject(), "Ctrl+N"), Item("_Open project…", () => _ = Open(), "Ctrl+O"), recent, new Separator(), ProjectItem("_Save", () => _ = Save(), "Ctrl+S"), ProjectItem("Save _as…", () => _ = Save(true), "Ctrl+Shift+S"), new Separator(), ProjectItem("Export website / game JSON…", () => _ = ExportWeb()), ProjectItem("Export runtime companion…", () => _ = ExportRuntimeCompanion()), ProjectItem("Engine export folder…", () => _ = ConfigureEngineExportFolderAsync()), ProjectItem("Export script…", () => _ = ExportScript()), ProjectItem("Export graph image…", () => _ = ExportGraph()), ProjectItem("Export translation CSV…", () => _ = ExportLocalizationAsync()), ProjectItem("Import translation CSV…", () => _ = ImportLocalizationAsync()), new Separator(), Item("Recovery saves…", () => _ = ShowRecoveries()), ProjectItem("Project details…", () => _ = ProjectDetails()), new Separator(), Item("E_xit", Close) };
        var paste = Item("Paste scenes", () => _ = PasteScenes()); paste.IsEnabled = copiedScenes is not null;
        var copySelected = Item("Copy selected scenes", CopySelectedScenes); copySelected.IsEnabled = selectedScenes.Count > 0;
        var edit = new MenuItem { Header = "_Edit", ItemsSource = new object[] { Item("_Undo", Undo, "Ctrl+Z"), Item("_Redo", Redo, "Ctrl+Shift+Z"), Item("_Find & replace…", () => _ = ShowFindReplace(), "Ctrl+H"), new Separator(), copySelected, Item("Copy scenes…", () => _ = CopyScenes()), paste, Item("Scene & choice templates…", () => _ = ShowTemplates()), new Separator(), Item("Add scene…", () => _ = AddScene()), Item("_Duplicate scene", DuplicateScene, "Ctrl+D"), Item("Rename scene ID…", () => _ = RenameScene()), Item("Delete scene…", () => _ = DeleteScene()), Item("Set scene as start", SetStart) } };
        var view = new MenuItem { Header = "_View", ItemsSource = new object[] { ProjectItem("Story graph", () => SetView(false)), ProjectItem("Script", () => SetView(true)), ProjectItem("Fit story", Graph.Fit, "Ctrl+0"), ProjectItem(preferences.ShowMinimap ? "Hide minimap" : "Show minimap", ToggleMinimap, "Ctrl+Shift+M"), ProjectItem("Groups & chapters…", () => _ = ShowGroups(), "Ctrl+Shift+G"), ProjectItem("Diagnostics", ToggleProblems), new Separator(), Item("Appearance & accessibility…", () => _ = ShowSettings()) } };
        MenuItem Toggle(string label, bool on, bool enabled, Action action) { var item = Item(label, action); item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = on; item.IsEnabled = enabled; return item; }
        var playEngine = Item("Play in engine", PlayInEngine); playEngine.IsEnabled = liveLink is not null;
        var story = new MenuItem { Header = "_Story", ItemsSource = new object[] { Item("Play from start", () => _ = Play(), "F5"), Item("Play from selected scene", () => _ = Play(true), "Shift+F5"), Item("Test scenarios…", () => _ = ShowScenariosAsync()), Item("Variables & characters…", () => _ = ShowVariables()), Item("Validate story", ToggleProblems), new Separator(), Toggle("Live link", liveLink is not null, true, () => SetLiveLink(liveLink is null)), Toggle("Live edits", liveEdits, liveLink is not null, () => LiveEdits = !liveEdits), playEngine, Toggle("Follow pane", followPane.IsVisible, liveLink is not null, () => { followPane.IsVisible = !followPane.IsVisible; followHidden = !followPane.IsVisible; RefreshFollow(); BuildMenu(); }) } };
        edit.IsEnabled = hasProject; story.IsEnabled = hasProject;
        var help = new MenuItem { Header = "_Help", ItemsSource = new object[] { Item("Guide & shortcuts", () => _ = ShowHelp(), "F1"), Item("About Dialogue Machine", () => _ = Ui.Ask(this, "Dialogue Machine", "Desktop edition 1.4.0 · Built with C# and Avalonia.\nAn offline workspace for branching stories. Your projects stay on your computer.", "Close")) } };
        C<Menu>("AppMenu").ItemsSource = new object[] { file, edit, view, story, help };
    }
    private void SetView(bool script)
    {
        C<Control>("ScriptScroll").IsVisible = script; Graph.IsVisible = !script;
        foreach (var name in new[] { "GraphHint", "ZoomBar", "GraphLegend" }) C<Border>(name).IsVisible = !script;
        UpdateGraphOverlays();
        C<Button>("GraphButton").Background = Ui.Brush(script ? "Base" : "Selection"); C<Button>("ScriptButton").Background = Ui.Brush(script ? "Selection" : "Base");
        if (!script) scriptDirty = true;
        RefreshScript();
    }
    private void SelectScene(DialogueState s, bool centre)
        => SelectScenes([s], s, centre);
    private void SelectScenes(IEnumerable<DialogueState> scenes, DialogueState? active, bool centre)
    {
        var next = scenes.ToList();
        Commit();
        var available = project.ListOfCharacterStates.ToHashSet();
        selectedScenes.Clear(); selectedScenes.UnionWith(next.Where(available.Contains));
        selected = active is not null && selectedScenes.Contains(active) ? active : project.ListOfCharacterStates.FirstOrDefault(selectedScenes.Contains);
        var index = selected is null ? -1 : project.ListOfCharacterStates.IndexOf(selected);
        if (index >= 0 && index < navigation.Scenes.Count && !SceneNavigation.Matches(navigation.Scenes[index], CurrentOutlineFilter())) ClearOutlineFilters();
        Graph.SetSelection(selectedScenes, selected); SyncOutlineSelection(); RefreshEditor(); UpdateGraphSelection(); BuildMenu();
        if (centre && selected is not null) Graph.Centre(selected);
    }
    private void SwitchTab(string tab) { Commit(); sceneEditor.Tab = tab; RefreshEditor(); }
    private void Edited()
    {
        ClearScenarioResults();
        pendingEdits = true;
        if (selected is not null)
        {
            var words = CountWords(selected.Text); wordCount += words - sceneWords.GetValueOrDefault(selected); sceneWords[selected] = words;
        }
        editTimer.Stop(); editTimer.Start(); UpdateStatus(); Graph.ClearTextLayouts(); Graph.InvalidateVisual(); scriptDirty = true; LiveStoryEdited();
    }
    private void Commit() => Commit(true);
    private void Commit(bool diagnostics, bool force = false)
    {
        if (!hasProject) return;
        Graph.FinishInteraction();
        editTimer.Stop();
        if (!pendingEdits && !force) return;
        var changed = history.Commit(project);
        pendingEdits = false;
        if (changed)
        {
            ClearScenarioResults();
            RebuildStatistics(); scriptDirty = true; LiveStoryEdited();
            if (diagnostics) RefreshDiagnostics();
        }
        UpdateStatus();
    }
    private void Mutate(Action action)
    {
        if (!hasProject) return;
        Commit(true, force: true); action(); pendingEdits = true; Commit(false); Refresh();
    }
    private void Undo()
    {
        if (!hasProject) return;
        Commit(true, force: true); var p = history.Undo(); if (p is null) return;
        ReplaceHistoryProject(p); Status("Undid the last edit.");
    }
    private void Redo()
    {
        if (!hasProject) return;
        Commit(true, force: true); var p = history.Redo(); if (p is null) return;
        ReplaceHistoryProject(p); Status("Redid the last edit.");
    }
    private void ReplaceHistoryProject(DialogueProject p)
    {
        // Include duplicate-ID occurrence so repairable imported scenes keep distinct selections.
        var keys = new HashSet<(string Id, int Occurrence)>(); (string, int)? active = null;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var scene in project.ListOfCharacterStates)
        {
            var key = (scene.State, occurrences.GetValueOrDefault(scene.State)); occurrences[scene.State] = key.Item2 + 1;
            if (selectedScenes.Contains(scene)) keys.Add(key);
            if (scene == selected) active = key;
        }
        project = p; pendingEdits = false; selectedScenes.Clear(); selected = null; occurrences.Clear();
        ClearScenarioResults();
        foreach (var scene in project.ListOfCharacterStates)
        {
            var key = (scene.State, occurrences.GetValueOrDefault(scene.State)); occurrences[scene.State] = key.Item2 + 1;
            if (keys.Contains(key)) selectedScenes.Add(scene);
            if (active == key) selected = scene;
        }
        if (keys.Count > 0) selected ??= project.ListOfCharacterStates.FirstOrDefault(selectedScenes.Contains) ?? project.ListOfCharacterStates.FirstOrDefault();
        Refresh(); LiveStoryEdited();
    }
    private void Refresh()
    {
        var available = project.ListOfCharacterStates.ToHashSet();
        selectedScenes.RemoveWhere(s => !available.Contains(s));
        if (selected is null) selectedScenes.Clear();
        else if (!selectedScenes.Contains(selected)) { selectedScenes.Clear(); selectedScenes.Add(selected); }
        RebuildStatistics(); scriptDirty = true;
        Graph.Project = project; Graph.SetSelection(selectedScenes, selected); UpdateGraphSelection(); RefreshDiagnostics(); RefreshEditor(); RefreshScript(); UpdateStatus(); BuildMenu();
        C<Button>("GraphButton").Background = Ui.Brush(Graph.IsVisible ? "Selection" : "Base");
        C<Button>("ScriptButton").Background = Ui.Brush(Graph.IsVisible ? "Base" : "Selection");
        UpdateGraphOverlays();
    }
    private void RefreshOutline()
    {
        refreshing = true;
        var filter = CurrentOutlineFilter();
        var items = navigation.Filter(filter);
        var list = C<ListBox>("SceneList");
        list.ItemTemplate ??= new FuncDataTemplate<NavigationScene>((result, _) =>
        {
            if (result is null) return null; // Recycling clears a container's content first.
            var s = project.ListOfCharacterStates[result.Index];
            var head = Ui.Text((s.State == project.Entry ? "◆  " : "●  ") + s.Title, 13); head.FontWeight = Avalonia.Media.FontWeight.SemiBold;
            var sub = Ui.Text(s.Speaker + "  ·  " + (s.Choices.Count > 0 ? $"{s.Choices.Count} choices" : s.DefaultNextId == "[EXIT]" ? "Ending" : "Continue"), 11, true);
            return Ui.Stack(head, sub);
        });
        list.SelectedItems!.Clear(); list.ItemsSource = items;
        foreach (var item in items.Where(i => selectedScenes.Contains(project.ListOfCharacterStates[i.Index]))) list.SelectedItems!.Add(item);
        C<TextBlock>("SceneCount").Text = items.Count == project.ListOfCharacterStates.Count ? items.Count.ToString() : $"{items.Count}/{project.ListOfCharacterStates.Count}";
        C<TextBlock>("OutlineEmpty").IsVisible = hasProject && items.Count == 0;
        C<Button>("ClearFiltersButton").IsVisible = filter != new OutlineFilter(); refreshing = false;
    }
    private void SyncOutlineSelection()
    {
        // Keep native list focus/range anchors when only the selection changes.
        var list = C<ListBox>("SceneList");
        var visible = list.Items.OfType<NavigationScene>().Where(i => selectedScenes.Contains(project.ListOfCharacterStates[i.Index])).ToList();
        var current = list.SelectedItems!;
        if (current.Cast<NavigationScene>().ToHashSet().SetEquals(visible)) return;
        refreshing = true; current.Clear(); foreach (var item in visible) current.Add(item); refreshing = false;
    }
    private void UpdateGraphSelection()
    {
        C<TextBlock>("GraphSelectionSummary").Text = selectedScenes.Count == 0 ? "No scenes selected" : $"{selectedScenes.Count} {(selectedScenes.Count == 1 ? "scene" : "scenes")} selected";
    }
    private void UpdateStatus()
    {
        C<StackPanel>("ProjectActions").IsVisible = hasProject;
        if (!hasProject)
        {
            Title = "Dialogue Machine";
            C<TextBlock>("ProjectTitle").Text = "Welcome";
            C<TextBlock>("ProjectSubtitle").Text = "Your stories, ready when you are";
            C<TextBlock>("StatsText").Text = "Offline workspace";
            Status("New project · Ctrl/Cmd+N    Open project · Ctrl/Cmd+O"); return;
        }
        // Pending text is immediately marked unsaved; exact equality is checked at commit,
        // recovery and explicit HasUnsavedChanges queries, without serializing on each key.
        var dirty = pendingEdits || history.CurrentSnapshot != savedSnapshot;
        C<TextBlock>("ProjectTitle").Text = project.ProjectName + (dirty ? " •" : "");
        C<TextBlock>("ProjectSubtitle").Text = filePath is null ? isExample ? "Example story · save a copy to make it yours" : "Unsaved project · save to choose a file location" : Path.GetFileName(filePath) + "  ·  " + project.CharacterId;
        Title = project.ProjectName + (dirty ? " *" : "") + " — Dialogue Machine";
        C<Button>("UndoButton").IsEnabled = history.CanUndo; C<Button>("RedoButton").IsEnabled = history.CanRedo;
        C<Button>("PlayButton").IsEnabled = project.ListOfCharacterStates.Count > 0;
        C<TextBlock>("StatsText").Text = $"{project.ListOfCharacterStates.Count} scenes   ·   {choiceCount} choices   ·   {wordCount} words";
        Status(dirty ? "Unsaved changes" + (preferences.RecoveryEnabled ? " · recovery saves every 10 seconds" : " · automatic recovery is off") : isExample ? "Example story · edits can be saved as your own project" : "All changes saved · " + filePath);
    }
    private static int CountWords(string text)
    {
        var count = 0; var inWord = false;
        foreach (var c in text) { if (char.IsWhiteSpace(c)) inWord = false; else if (!inWord) { count++; inWord = true; } }
        return count;
    }
    private void RebuildStatistics()
    {
        wordCount = choiceCount = 0; sceneWords.Clear();
        foreach (var scene in project.ListOfCharacterStates) { var words = CountWords(scene.Text); sceneWords[scene] = words; wordCount += words; choiceCount += scene.Choices.Count; }
    }
    private void Status(string text) => C<TextBlock>("StatusText").Text = text;
    private void RefreshDiagnostics()
    {
        var issues = DialogueEngine.Validate(project);
        navigation = new SceneNavigation(project, issues); RefreshOutlineFilters();
        C<TextBlock>("DiagnosticsTitle").Text = issues.Count == 0 ? "✓  Story is ready to play" : $"{issues.Count(i => i.Severity == "Error")} errors · {issues.Count(i => i.Severity == "Warning")} warnings";
        C<Button>("ProblemsButton").Content = issues.Count == 0 ? "✓   Story diagnostics" : $"◇   Diagnostics ({issues.Count})";
        C<ListBox>("DiagnosticsList").ItemsSource = issues.Select(d => new ListBoxItem { Tag = d, Content = Ui.Stack(Ui.Text(d.Severity + " · " + d.StateId, 12), Ui.Text(d.Message, 12, true)) }).ToList();
    }
    private void ToggleProblems() { Commit(); RefreshDiagnostics(); var b = C<Border>("DiagnosticsBorder"); b.IsVisible = !b.IsVisible; }
    private async Task AddScene()
    {
        var title = await Ui.Prompt(this, "Add a scene", "Give this scene a short, descriptive title.", "New scene"); if (string.IsNullOrWhiteSpace(title)) return;
        Mutate(() => { var s = Templates.Scene("scene_" + Guid.NewGuid().ToString("N")[..8], title.Trim(), selected?.Speaker ?? "Character", "", (selected?.VisualX ?? 0) + 350, selected?.VisualY ?? 100); project.ListOfCharacterStates.Add(s); selected = s; }); Graph.Centre(selected!);
    }
    private void DuplicateScene()
    {
        if (selected is null) return;
        _ = AuthoringAction(() => Mutate(() =>
        {
            var source = selected;
            var copy = AuthoringOperations.PasteScenes(project, AuthoringOperations.CopyScenes([source]), source.VisualX + 45, source.VisualY + 65)[0];
            copy.Metadata["title"] = source.Title + " (copy)"; selected = copy;
        }));
    }
    private async Task RenameScene()
    {
        if (selected is null) return;
        var id = await Ui.Prompt(this, "Rename scene ID", "All connections to this scene will update automatically.", selected.State);
        if (id is null) return;
        try { Mutate(() => DialogueEngine.Rename(project, selected, id.Trim())); } catch (ArgumentException ex) { await Ui.Ask(this, "Cannot rename scene", ex.Message, "Close"); }
    }
    private void SetStart() { if (selected is not null) Mutate(() => project.EntryStateId = selected.State); }
    private async Task DeleteScene()
    {
        if (selected is null) return; var target = selected;
        var incoming = project.ListOfCharacterStates.Sum(s => (s.DefaultNextId == target.State ? 1 : 0) + s.Choices.Count(c => c.NextStateId == target.State));
        if (await Ui.Ask(this, "Delete ‘" + target.Title + "’?", $"{incoming} connection(s) point to this scene. These connections will become endings. You can undo this deletion.", "Delete scene", "Cancel") != "Delete scene") return;
        Mutate(() => { SceneGroups.RemoveScene(project, target); foreach (var s in project.ListOfCharacterStates) { if (s.DefaultNextId == target.State) s.DefaultNextId = "[EXIT]"; foreach (var c in s.Choices) if (c.NextStateId == target.State) c.NextStateId = "[EXIT]"; } if (project.EntryStateId == target.State) project.EntryStateId = project.ListOfCharacterStates.FirstOrDefault()?.State; selected = project.ListOfCharacterStates.FirstOrDefault(); });
    }
    private async Task Play(bool fromSelected = false)
    {
        Commit(); RefreshDiagnostics();
        if (DialogueEngine.Validate(project).Any(i => i.Severity == "Error")) { C<Border>("DiagnosticsBorder").IsVisible = true; Status("Resolve the story errors before playtesting. Select an issue to open its scene."); return; }
        if (project.ListOfCharacterStates.Count == 0) return;
        var playtest = new PlaytestWindow(project, fromSelected ? selected?.State : null, preferences.Scale);
        await playtest.ShowDialog(this);
        SetCoverage(ScenarioReplay.Coverage(project, playtest.Session.Trace));
    }
}
