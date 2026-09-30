using Avalonia;
using Avalonia.Controls;
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
    private readonly ProjectHistory history = new();
    private string savedSnapshot = "";
    private string? filePath;
    private bool refreshing, allowClose, closePending, hasProject;
    private string editorTab = "Content";
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
        InitializeComponent();
        ReadPreferences();
        history.Reset(project); savedSnapshot = ProjectStore.Serialize(project);
        WireActions(); WireWelcome(); BuildMenu(); Refresh(); RefreshWelcome();
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
        AsyncClick("AddButton", AddScene); AsyncClick("VariablesButton", ShowVariables); Click("ProblemsButton", ToggleProblems);
        Click("ContentTab", () => SwitchTab("Content")); Click("LogicTab", () => SwitchTab("Logic")); Click("NotesTab", () => SwitchTab("Notes"));
        Click("GraphButton", () => SetView(false)); Click("ScriptButton", () => SetView(true));
        Click("LayoutButton", () => { Mutate(() => DialogueEngine.AutoLayout(project)); Graph.Fit(); Status("Scenes arranged by their connections."); });
        Click("ZoomIn", () => Graph.SetZoom(Graph.Zoom * 1.2)); Click("ZoomOut", () => Graph.SetZoom(Graph.Zoom / 1.2)); Click("FitButton", Graph.Fit);
        Click("CloseDiagnostics", () => C<Border>("DiagnosticsBorder").IsVisible = false);
        C<TextBox>("SearchBox").TextChanged += (_, _) => RefreshOutline();
        C<ListBox>("SceneList").SelectionChanged += (_, _) =>
        {
            if (!refreshing && C<ListBox>("SceneList").SelectedItem is ListBoxItem item && item.Tag is DialogueState s) SelectScene(s, true);
        };
        Graph.SceneSelected += s => SelectScene(s, false);
        Graph.Changed += () => { Commit(); Refresh(); };
        Graph.ZoomChanged += z => C<TextBlock>("ZoomLabel").Text = $"{z:P0}";
        Graph.SizeChanged += (_, _) => C<Border>("GraphLegend").IsVisible = Graph.IsVisible && Graph.Bounds.Width >= 650;
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
                case Key.F: C<TextBox>("SearchBox").Focus(); C<TextBox>("SearchBox").SelectAll(); break;
                case Key.Z: if (shift) Redo(); else Undo(); break;
                case Key.Y: Redo(); break;
                case Key.D: DuplicateScene(); break;
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
        file.ItemsSource = new object[] { Item("_Home", () => _ = ReturnHomeAsync()), Item("_New project…", () => _ = NewProject(), "Ctrl+N"), Item("_Open project…", () => _ = Open(), "Ctrl+O"), recent, new Separator(), ProjectItem("_Save", () => _ = Save(), "Ctrl+S"), ProjectItem("Save _as…", () => _ = Save(true), "Ctrl+Shift+S"), new Separator(), ProjectItem("Export website / game JSON…", () => _ = ExportWeb()), ProjectItem("Export script…", () => _ = ExportScript()), ProjectItem("Export graph image…", () => _ = ExportGraph()), new Separator(), Item("Recovery saves…", () => _ = ShowRecoveries()), ProjectItem("Project details…", () => _ = ProjectDetails()), new Separator(), Item("E_xit", Close) };
        var edit = new MenuItem { Header = "_Edit", ItemsSource = new object[] { Item("_Undo", Undo, "Ctrl+Z"), Item("_Redo", Redo, "Ctrl+Shift+Z"), new Separator(), Item("Add scene…", () => _ = AddScene()), Item("_Duplicate scene", DuplicateScene, "Ctrl+D"), Item("Rename scene ID…", () => _ = RenameScene()), Item("Delete scene…", () => _ = DeleteScene()), Item("Set scene as start", SetStart) } };
        var view = new MenuItem { Header = "_View", ItemsSource = new object[] { ProjectItem("Story graph", () => SetView(false)), ProjectItem("Script", () => SetView(true)), ProjectItem("Fit story", Graph.Fit, "Ctrl+0"), ProjectItem("Diagnostics", ToggleProblems), new Separator(), Item("Appearance & accessibility…", () => _ = ShowSettings()) } };
        var story = new MenuItem { Header = "_Story", ItemsSource = new object[] { Item("Play from start", () => _ = Play(), "F5"), Item("Play from selected scene", () => _ = Play(true), "Shift+F5"), Item("Variables & characters…", () => _ = ShowVariables()), Item("Validate story", ToggleProblems) } };
        edit.IsEnabled = hasProject; story.IsEnabled = hasProject;
        var help = new MenuItem { Header = "_Help", ItemsSource = new object[] { Item("Guide & shortcuts", () => _ = ShowHelp(), "F1"), Item("About Dialogue Machine", () => _ = Ui.Ask(this, "Dialogue Machine", "Desktop edition 1.1 · Built with C# and Avalonia.\nAn offline workspace for branching stories. Your projects stay on your computer.", "Close")) } };
        C<Menu>("AppMenu").ItemsSource = new object[] { file, edit, view, story, help };
    }
    private void SetView(bool script)
    {
        C<ScrollViewer>("ScriptScroll").IsVisible = script; Graph.IsVisible = !script;
        foreach (var name in new[] { "GraphHint", "ZoomBar", "GraphLegend" }) C<Border>(name).IsVisible = !script;
        C<Border>("GraphLegend").IsVisible = !script && Graph.Bounds.Width >= 650;
        C<Button>("GraphButton").Background = Ui.Brush(script ? "Base" : "Selection"); C<Button>("ScriptButton").Background = Ui.Brush(script ? "Selection" : "Base");
        if (script) RefreshScript();
    }
    private void SelectScene(DialogueState s, bool centre)
    {
        Commit(); selected = s; Graph.Selected = s; RefreshOutline(); RefreshEditor(); Graph.InvalidateVisual();
        if (centre) Graph.Centre(s);
    }
    private void SwitchTab(string tab) { Commit(); editorTab = tab; RefreshEditor(); }
    private void Edited()
    {
        editTimer.Stop(); editTimer.Start(); UpdateStatus(); Graph.InvalidateVisual();
    }
    private void Commit()
    {
        if (!hasProject) return;
        editTimer.Stop(); if (history.Commit(project)) RefreshDiagnostics(); UpdateStatus();
    }
    private void Mutate(Action action)
    {
        if (!hasProject) return;
        Commit(); action(); Commit(); Refresh();
    }
    private void Undo()
    {
        if (!hasProject) return;
        Commit(); var p = history.Undo(); if (p is null) return;
        ReplaceHistoryProject(p); Status("Undid the last edit.");
    }
    private void Redo()
    {
        if (!hasProject) return;
        Commit(); var p = history.Redo(); if (p is null) return;
        ReplaceHistoryProject(p); Status("Redid the last edit.");
    }
    private void ReplaceHistoryProject(DialogueProject p)
    {
        var id = selected?.State; project = p; selected = project.ListOfCharacterStates.FirstOrDefault(s => s.State == id) ?? project.ListOfCharacterStates.FirstOrDefault(); Refresh();
    }
    private void Refresh()
    {
        Graph.Project = project; Graph.Selected = selected; Graph.InvalidateVisual(); RefreshOutline(); RefreshEditor(); RefreshScript(); RefreshDiagnostics(); UpdateStatus();
        C<Button>("GraphButton").Background = Ui.Brush(Graph.IsVisible ? "Selection" : "Base");
        C<Button>("ScriptButton").Background = Ui.Brush(Graph.IsVisible ? "Base" : "Selection");
    }
    private void RefreshOutline()
    {
        refreshing = true;
        var filter = C<TextBox>("SearchBox").Text ?? "";
        var items = new List<ListBoxItem>();
        foreach (var s in project.ListOfCharacterStates.Where(s => (s.Title + " " + s.State + " " + s.Text + " " + s.Speaker + " " + string.Join(" ", s.Choices.Select(c => c.ButtonText))).Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var head = Ui.Text((s.State == project.Entry ? "◆  " : "●  ") + s.Title, 13); head.FontWeight = Avalonia.Media.FontWeight.SemiBold;
            var sub = Ui.Text(s.Speaker + "  ·  " + (s.Choices.Count > 0 ? $"{s.Choices.Count} choices" : s.DefaultNextId == "[EXIT]" ? "Ending" : "Continue"), 11, true);
            var item = new ListBoxItem { Content = Ui.Stack(head, sub), Tag = s }; items.Add(item);
        }
        var list = C<ListBox>("SceneList"); list.ItemsSource = items; list.SelectedItem = items.FirstOrDefault(i => i.Tag == selected);
        C<TextBlock>("SceneCount").Text = project.ListOfCharacterStates.Count.ToString(); refreshing = false;
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
        C<TextBlock>("ProjectTitle").Text = project.ProjectName + (Dirty ? " •" : "");
        C<TextBlock>("ProjectSubtitle").Text = filePath is null ? isExample ? "Example story · save a copy to make it yours" : "Unsaved project · save to choose a file location" : Path.GetFileName(filePath) + "  ·  " + project.CharacterId;
        Title = project.ProjectName + (Dirty ? " *" : "") + " — Dialogue Machine";
        C<Button>("UndoButton").IsEnabled = history.CanUndo; C<Button>("RedoButton").IsEnabled = history.CanRedo;
        C<Button>("PlayButton").IsEnabled = project.ListOfCharacterStates.Count > 0;
        var words = project.ListOfCharacterStates.Sum(s => s.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        C<TextBlock>("StatsText").Text = $"{project.ListOfCharacterStates.Count} scenes   ·   {project.ListOfCharacterStates.Sum(s => s.Choices.Count)} choices   ·   {words} words";
        Status(Dirty ? "Unsaved changes" + (preferences.RecoveryEnabled ? " · recovery saves every 10 seconds" : " · automatic recovery is off") : isExample ? "Example story · edits can be saved as your own project" : "All changes saved · " + filePath);
    }
    private void Status(string text) => C<TextBlock>("StatusText").Text = text;
    private void RefreshDiagnostics()
    {
        var issues = DialogueEngine.Validate(project);
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
        Mutate(() => { var copy = ProjectStore.Clone(new DialogueProject { ListOfCharacterStates = [selected] }).ListOfCharacterStates[0]; var old = copy.State; copy.State = "scene_" + Guid.NewGuid().ToString("N")[..8]; copy.Metadata["title"] = selected.Title + " (copy)"; copy.VisualX += 45; copy.VisualY += 65; foreach (var c in copy.Choices) { c.InternalId = Guid.NewGuid(); if (c.NextStateId == old) c.NextStateId = copy.State; } if (copy.DefaultNextId == old) copy.DefaultNextId = copy.State; project.ListOfCharacterStates.Add(copy); selected = copy; });
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
        Mutate(() => { project.ListOfCharacterStates.Remove(target); foreach (var s in project.ListOfCharacterStates) { if (s.DefaultNextId == target.State) s.DefaultNextId = "[EXIT]"; foreach (var c in s.Choices) if (c.NextStateId == target.State) c.NextStateId = "[EXIT]"; } if (project.EntryStateId == target.State) project.EntryStateId = project.ListOfCharacterStates.FirstOrDefault()?.State; selected = project.ListOfCharacterStates.FirstOrDefault(); });
    }
    private async Task Play(bool fromSelected = false)
    {
        Commit(); RefreshDiagnostics();
        if (DialogueEngine.Validate(project).Any(i => i.Severity == "Error")) { C<Border>("DiagnosticsBorder").IsVisible = true; Status("Resolve the story errors before playtesting. Select an issue to open its scene."); return; }
        if (project.ListOfCharacterStates.Count == 0) return;
        await new PlaytestWindow(project, fromSelected ? selected?.State : null).ShowDialog(this);
    }
}
