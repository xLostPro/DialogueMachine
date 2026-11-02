using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine.Core;
using DialogueMachine.UI;
using System.Text.Json;

internal static class GraphOrganizationChecks
{
    public static void Core(Action<bool, string> check, Action<Action, string> throws, string output)
    {
        var project = Templates.Harbour(); var scenes = project.ListOfCharacterStates;
        var website = ProjectStore.StoryExport(project);
        var chapter = SceneGroups.Create(project, "  Chapter one  "); var subplot = SceneGroups.Create(project, "Subplot");
        check(chapter.Name == "Chapter one" && chapter.Id != subplot.Id, "group names trim and each group has a stable distinct identity");
        SceneGroups.Assign(project, chapter, [scenes[0], scenes[1], scenes[0]]);
        SceneGroups.Assign(project, subplot, [scenes[1], scenes[2]]);
        check(SceneGroups.Members(project, chapter).SequenceEqual(scenes.Take(2)) && chapter.Scenes.Count == 2, "group assignment deduplicates a selected set and allows overlapping chapters");
        var beforeInvalid = ProjectStore.Serialize(project);
        throws(() => SceneGroups.Create(project, "chapter ONE"), "creating a group rejects name collisions regardless of case");
        throws(() => SceneGroups.Rename(project, subplot, "Chapter one"), "renaming a group rejects another group's name");
        throws(() => SceneGroups.Create(project, "  "), "creating a group rejects a blank name");
        throws(() => SceneGroups.Assign(project, chapter, [scenes[0], new DialogueState()]), "stale scene-set assignment rejects the complete operation");
        throws(() => SceneGroups.Remove(project, chapter, [new DialogueState()]), "stale scene-set removal leaves membership unchanged");
        check(ProjectStore.Serialize(project) == beforeInvalid, "rejected group operations preserve every project field");
        chapter.Extra = new() { ["FutureGroup"] = JsonSerializer.SerializeToElement(42) };
        chapter.Scenes[0].Extra = new() { ["FutureMember"] = JsonSerializer.SerializeToElement("keep") };
        var cloned = ProjectStore.Clone(project);
        check(cloned.SceneGroups[0].Id == chapter.Id && cloned.SceneGroups[0].Extra!["FutureGroup"].GetInt32() == 42 && cloned.SceneGroups[0].Scenes[0].Extra!["FutureMember"].GetString() == "keep", "desktop groups and unknown group/member fields survive cloning");
        var savePath = Path.Combine(output, "groups.dialogue.json"); ProjectStore.AtomicWrite(savePath, ProjectStore.Serialize(project));
        check(ProjectStore.Serialize(ProjectStore.Load(savePath)) == ProjectStore.Serialize(project), "group identities and overlapping memberships survive save/load");
        var recovery = new RecoveryStore(Path.Combine(output, "group-recovery-" + Guid.NewGuid().ToString("N"))); recovery.Save(Guid.NewGuid().ToString("N"), new RecoveryRecord(null, DateTimeOffset.UtcNow, project));
        var recovered = recovery.FindUnsaved().Single().Record.Project;
        check(recovered.SceneGroups[0].Name == chapter.Name && SceneGroups.Members(recovered, recovered.SceneGroups[0]).Count == 2, "recovery discovers and restores complete scene groups");
        check(ProjectStore.StoryExport(project) == website && !website.Contains("SceneGroups"), "groups do not alter the story JSON schema, scene metadata or numeric visibility");
        var nav = new SceneNavigation(project, []);
        check(nav.Filter(new OutlineFilter(GroupId: chapter.Id)).Select(s => s.Index).SequenceEqual(new[] { 0, 1 }) && nav.Filter(new OutlineFilter(GroupId: "")).Count == 2, "outline group and ungrouped filters use cached memberships");
        check(nav.Filter(new OutlineFilter(Query: scenes[1].State, Speaker: scenes[1].Speaker, GroupId: chapter.Id)).Single().Index == 1, "group filtering intersects the existing outline search and speaker filters");
        SceneGroups.Remove(project, chapter, [scenes[1]]);
        check(SceneGroups.Members(project, chapter).SequenceEqual([scenes[0]]) && SceneGroups.Members(project, subplot).Contains(scenes[1]), "removing a set affects only the chosen group's memberships");
        SceneGroups.Rename(project, chapter, "Opening"); check(chapter.Name == "Opening" && chapter.Id == cloned.SceneGroups[0].Id, "renaming a chapter preserves its identity and members");
        DialogueEngine.Rename(project, scenes[0], "opening_scene");
        check(SceneGroups.Members(project, chapter).Single() == scenes[0] && chapter.Scenes[0].StateId == "opening_scene" && chapter.Scenes[0].Extra!["FutureMember"].GetString() == "keep", "scene-ID rename rebinds memberships while preserving extension data");
        var sceneData = JsonSerializer.Serialize(project.ListOfCharacterStates, ProjectStore.Options); var entry = project.Entry;
        SceneGroups.Delete(project, chapter);
        check(JsonSerializer.Serialize(project.ListOfCharacterStates, ProjectStore.Options) == sceneData && project.Entry == entry && project.SceneGroups.Single() == subplot, "deleting a group preserves all scenes, connections, entry and other groups");
        throws(() => SceneGroups.Assign(project, chapter, [scenes[0]]), "deleted groups cannot receive later assignments");
        SceneGroups.Assign(project, subplot, scenes.Take(2));
        var bundle = AuthoringOperations.CopyScenes(scenes.Take(2));
        var sameProject = AuthoringOperations.PasteScenes(project, bundle, 500, 500);
        check(sameProject.All(s => !SceneGroups.Members(project, subplot).Contains(s)) && project.SceneGroups.Count == 1, "pasting inside the source project creates ungrouped copies without changing groups");
        var other = Templates.Blank(); var otherGroup = SceneGroups.Create(other, "Subplot"); SceneGroups.Assign(other, otherGroup, other.ListOfCharacterStates);
        var crossProject = AuthoringOperations.PasteScenes(other, bundle, 10, 10);
        check(other.SceneGroups.Count == 1 && SceneGroups.Members(other, otherGroup).Count == 1 && crossProject[0].Choices[0].NextStateId == crossProject[1].State, "cross-project paste retains internal links and never matches memberships by name");
        AuthoringOperations.SaveSceneTemplate(project, scenes[1], "Grouped scene");
        var templateCopy = AuthoringOperations.InsertSceneTemplate(project, project.SceneTemplates[0], 900, 900);
        check(!SceneGroups.Members(project, subplot).Contains(templateCopy), "inserting a grouped scene's template creates an ungrouped scene");
        var duplicate = Templates.Blank(); var first = duplicate.ListOfCharacterStates[0]; var second = Templates.Scene(first.State, "Second duplicate", "Character", "", 400, 80); duplicate.ListOfCharacterStates.Add(second);
        var duplicateGroup = SceneGroups.Create(duplicate, "Repair"); SceneGroups.Assign(duplicate, duplicateGroup, [second]);
        check(duplicateGroup.Scenes.Single().Occurrence == 1 && SceneGroups.Members(duplicate, duplicateGroup).Single() == second, "groups distinguish separate imported scenes with duplicate IDs");
        var duplicateCopy = ProjectStore.Clone(duplicate);
        check(SceneGroups.Members(duplicateCopy, duplicateCopy.SceneGroups[0]).Single() == duplicateCopy.ListOfCharacterStates[1], "duplicate-ID membership survives desktop round trips");
        DialogueEngine.Rename(duplicate, first, "repaired_first");
        check(duplicateGroup.Scenes.Single().Occurrence == 0 && SceneGroups.Members(duplicate, duplicateGroup).Single() == second, "repairing another duplicate ID shifts membership occurrences safely");
        first.State = second.State; duplicateGroup.Scenes[0].Occurrence = 1; SceneGroups.RemoveScene(duplicate, first);
        check(SceneGroups.Members(duplicate, duplicateGroup).Single() == second && duplicateGroup.Scenes[0].Occurrence == 0, "deleting an earlier duplicate retains membership of its surviving sibling");
        SceneGroups.RemoveScene(duplicate, second);
        check(duplicateGroup.Scenes.Count == 0 && duplicate.SceneGroups.Count == 1, "deleting the last member retains an empty chapter");
        var clean = ProjectStore.Parse("""{"SceneGroups":null,"ListOfCharacterStates":[]}""");
        check(clean.SceneGroups.Count == 0, "legacy files and null additive group collections remain openable");
        var sanitized = ProjectStore.Parse("""{"ListOfCharacterStates":[{"State":"a"}],"SceneGroups":[{"Id":"g","Name":"Group","Scenes":[{"StateId":"a"},{"StateId":"a"},{"StateId":"missing"}]}]}""");
        check(sanitized.SceneGroups[0].Scenes.Count == 1, "import sanitation removes duplicate and stale memberships without changing scenes");
        throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[],"SceneGroups":[null]}"""), "null scene groups fail safely during import");
        throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[],"SceneGroups":[{"Id":"g","Name":"A"},{"Id":"g","Name":"B"}]}"""), "duplicate group identities fail safely during import");
        throws(() => ProjectStore.Parse("""{"ListOfCharacterStates":[],"SceneGroups":[{"Id":"g","Name":"A","Scenes":[{"StateId":"a","Occurrence":-1}]}]}"""), "invalid group member occurrences fail safely during import");
        var historic = Templates.Harbour(); var history = new ProjectHistory(); history.Reset(historic);
        void HistoryStep(Action action, string description)
        {
            var before = ProjectStore.Serialize(historic); action(); check(history.Commit(historic), description + " records a change"); var after = ProjectStore.Serialize(historic);
            check(ProjectStore.Serialize(history.Undo()!) == before && ProjectStore.Serialize(history.Redo()!) == after, description + " restores the complete operation with one Undo/Redo");
        }
        var historicGroup = new SceneGroup();
        HistoryStep(() => historicGroup = SceneGroups.Create(historic, "Act I"), "group creation");
        HistoryStep(() => SceneGroups.Assign(historic, historicGroup, historic.ListOfCharacterStates.Take(3)), "scene-set assignment");
        HistoryStep(() => SceneGroups.Rename(historic, historicGroup, "Act II"), "group rename");
        HistoryStep(() => SceneGroups.Remove(historic, historicGroup, historic.ListOfCharacterStates.Take(2)), "scene-set removal");
        var noop = ProjectStore.Serialize(historic); SceneGroups.Assign(historic, historicGroup, [historic.ListOfCharacterStates[2]]);
        check(!history.Commit(historic) && ProjectStore.Serialize(historic) == noop, "reassigning current members creates no history step");
        HistoryStep(() => SceneGroups.Delete(historic, historicGroup), "group deletion");
    }
    public static void UI(Action<bool, string> check, string output, Action<Window, string> capture)
    {
        var folder = Path.Combine(output, "group-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var project = Templates.Harbour();
        for (var i = 0; i < project.ListOfCharacterStates.Count; i++) { project.ListOfCharacterStates[i].VisualX = i % 3 * 370; project.ListOfCharacterStates[i].VisualY = i / 3 * 340; }
        var path = Path.Combine(folder, "story.dialogue.json"); ProjectStore.AtomicWrite(path, ProjectStore.Serialize(project));
        var window = new MainWindow(null, true, Path.Combine(folder, "settings")); window.Show(); Pump(window.OpenProjectAsync(path));
        T Control<T>(string name) where T : Avalonia.Controls.Control => window.FindControl<T>(name)!;
        var graph = Control<GraphCanvas>("Graph"); var map = Control<GraphMinimap>("Minimap");
        void Key(Window target, Avalonia.Input.Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        { target.KeyPress(key, modifiers, PhysicalKey.None, ""); target.KeyRelease(key, modifiers, PhysicalKey.None, ""); Dispatcher.UIThread.RunJobs(); }
        void Select(int index, RawInputModifiers modifier = RawInputModifiers.None)
        {
            var scene = window.Project.ListOfCharacterStates[index];
            var point = graph.TranslatePoint(graph.WorldToViewport(new Point(scene.VisualX + 35, scene.VisualY + 95)), window)!.Value;
            window.MouseDown(point, MouseButton.Left, modifier); window.MouseUp(point, MouseButton.Left, modifier); Dispatcher.UIThread.RunJobs();
        }
        Window Groups() { Press(Control<Button>("GroupsButton")); return window.OwnedWindows.Single(w => w.IsVisible); }
        void Close(Window dialog) => Press(dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close")));
        void Undo() => Press(Control<Button>("UndoButton"));
        void Redo() => Press(Control<Button>("RedoButton"));
        void OneStep(string before, string label)
        {
            var after = ProjectStore.Serialize(window.Project); Undo(); check(ProjectStore.Serialize(window.Project) == before, label + " has one complete UI Undo step");
            Redo(); check(ProjectStore.Serialize(window.Project) == after, label + " has one complete UI Redo step");
        }
        Select(0); Select(1, RawInputModifiers.Control); var initial = ProjectStore.Serialize(window.Project);
        check(!Control<Border>("MinimapPanel").IsVisible, "minimap is optional and hidden by default");
        var dialog = Groups(); Named<TextBox>(dialog, "SceneGroupName").Text = "Canceled draft"; Close(dialog);
        check(ProjectStore.Serialize(window.Project) == initial && !window.HasUnsavedChanges, "closing a group name draft leaves the project and history unchanged");
        dialog = Groups(); Named<TextBox>(dialog, "SceneGroupName").Text = "Chapter one"; Press(Named<Button>(dialog, "CreateSceneGroup")); Close(dialog);
        check(graph.SelectedScenes.Count == 2 && graph.Selected == window.Project.ListOfCharacterStates[1] && window.Project.SceneGroups.Single().Scenes.Count == 0, "creating an empty chapter preserves the selected set and active editor scene");
        OneStep(initial, "chapter creation"); var groupId = window.Project.SceneGroups[0].Id;
        var before = ProjectStore.Serialize(window.Project); dialog = Groups(); Press(Named<Button>(dialog, "AssignSceneGroup")); Close(dialog); OneStep(before, "selected-set assignment");
        check(SceneGroups.Members(window.Project, window.Project.SceneGroups[0]).SequenceEqual(graph.SelectedScenes) && graph.Selected == window.Project.ListOfCharacterStates[1], "assigning scenes preserves both graph selection and active editor");
        before = ProjectStore.Serialize(window.Project); dialog = Groups(); Press(Named<Button>(dialog, "AssignSceneGroup")); Close(dialog);
        check(ProjectStore.Serialize(window.Project) == before, "repeating an unchanged assignment adds no project edit");
        var filter = Control<ComboBox>("GroupFilter"); filter.SelectedItem = filter.Items.Cast<object>().Single(o => o.ToString() == "Chapter one");
        before = ProjectStore.Serialize(window.Project); dialog = Groups(); Named<TextBox>(dialog, "SceneGroupName").Text = "Act I"; Press(Named<Button>(dialog, "RenameSceneGroup")); Close(dialog); OneStep(before, "chapter rename");
        check(window.Project.SceneGroups[0].Id == groupId && filter.SelectedItem!.ToString() == "Act I" && Control<ListBox>("SceneList").Items.Count == 2, "outline group filters follow a rename by stable group identity");
        Control<TextBox>("SearchBox").Text = window.Project.ListOfCharacterStates[0].Text; Dispatcher.UIThread.RunJobs();
        check(Control<ListBox>("SceneList").Items.Count == 1 && graph.SelectedScenes.Count == 2, "group and text filtering intersect while retaining hidden selections");
        Press(Control<Button>("ClearFiltersButton"));
        graph.Focus(); Key(window, Avalonia.Input.Key.Escape); graph.Fit(); Select(1); before = ProjectStore.Serialize(window.Project); dialog = Groups(); Press(Named<Button>(dialog, "RemoveSceneGroup")); Close(dialog); OneStep(before, "selected-set removal");
        check(window.Project.SceneGroups[0].Scenes.Count == 1 && graph.Selected == window.Project.ListOfCharacterStates[1], "removing a member preserves the active scene and remaining memberships");
        Undo(); Select(0); Select(1, RawInputModifiers.Control); Select(2, RawInputModifiers.Control);
        before = ProjectStore.Serialize(window.Project); dialog = Groups(); Press(Named<Button>(dialog, "SelectSceneGroup"));
        check(graph.SelectedScenes.Count == 2 && graph.Selected == window.Project.ListOfCharacterStates[0] && ProjectStore.Serialize(window.Project) == before, "selecting a group's scenes replaces the set without changing project data");
        Select(1); dialog = Groups(); Press(Named<Button>(dialog, "SelectSceneGroup"));
        check(graph.SelectedScenes.Count == 2 && graph.Selected == window.Project.ListOfCharacterStates[1], "group selection keeps the active editor scene when it is a member");
        dialog = Groups(); Press(Named<Button>(dialog, "FilterSceneGroup"));
        check(filter.SelectedItem!.ToString() == "Act I" && Control<ListBox>("SceneList").Items.Count == 2 && Control<Expander>("OutlineFilters").IsExpanded, "group manager exposes a keyboard-reachable outline filter action");
        var sceneSnapshot = JsonSerializer.Serialize(window.Project.ListOfCharacterStates, ProjectStore.Options); before = ProjectStore.Serialize(window.Project);
        dialog = Groups(); Press(Named<Button>(dialog, "DeleteSceneGroup")); Close(dialog);
        check(window.Project.SceneGroups.Count == 0 && JsonSerializer.Serialize(window.Project.ListOfCharacterStates, ProjectStore.Options) == sceneSnapshot && graph.SelectedScenes.Count == 2 && graph.Selected == window.Project.ListOfCharacterStates[1] && filter.SelectedItem!.ToString() == "Any group", "UI group deletion preserves scenes, links and selection and clears the deleted filter");
        OneStep(before, "chapter deletion"); Undo();
        graph.Focus(); Key(window, Avalonia.Input.Key.Right); var moved = window.Project.ListOfCharacterStates[0].VisualX;
        check(moved == project.ListOfCharacterStates[0].VisualX + 10 && window.Project.ListOfCharacterStates[1].VisualX == project.ListOfCharacterStates[1].VisualX + 10, "chapter selection still moves as the existing relative scene set"); Undo();
        dialog = Groups(); Named<TextBox>(dialog, "SceneGroupName").Text = "Empty chapter"; Press(Named<Button>(dialog, "CreateSceneGroup")); Press(Named<Button>(dialog, "SelectSceneGroup"));
        check(graph.SelectedScenes.Count == 0 && graph.Selected is null, "selecting an empty group clears graph and editor selection predictably");
        before = ProjectStore.Serialize(window.Project); dialog = Groups(); Named<ListBox>(dialog, "SceneGroupList").SelectedIndex = 1; Named<TextBox>(dialog, "SceneGroupName").Text = "Empty act"; Press(Named<Button>(dialog, "RenameSceneGroup")); Close(dialog); OneStep(before, "empty-selection group rename");
        check(graph.SelectedScenes.Count == 0 && graph.Selected is null, "Undo/Redo group operations preserve an explicitly empty selection and editor");
        dialog = Groups(); Named<ListBox>(dialog, "SceneGroupList").SelectedIndex = 0; Press(Named<Button>(dialog, "SelectSceneGroup"));
        graph.Focus(); Key(window, Avalonia.Input.Key.C, RawInputModifiers.Control); Key(window, Avalonia.Input.Key.V, RawInputModifiers.Control);
        check(graph.SelectedScenes.Count == 2 && graph.SelectedScenes.All(s => !SceneGroups.Members(window.Project, window.Project.SceneGroups[0]).Contains(s)), "graph copy/paste of a chapter selects fresh ungrouped scenes"); Undo();
        dialog = Groups(); Named<ListBox>(dialog, "SceneGroupList").SelectedIndex = 0; Press(Named<Button>(dialog, "SelectSceneGroup"));
        check(Pump(window.SaveProjectAsync()) && ProjectStore.Load(path).SceneGroups.Count == 2 && !window.HasUnsavedChanges, "actual desktop Save persists groups and clears dirty state");
        before = ProjectStore.Serialize(window.Project); var selectedIds = graph.SelectedScenes.Select(s => s.State).ToArray(); var activeId = graph.Selected!.State;
        graph.Focus(); Key(window, Avalonia.Input.Key.M, RawInputModifiers.Control | RawInputModifiers.Shift);
        check(Control<Border>("MinimapPanel").IsVisible && AutomationProperties.GetName(map)!.Contains("Arrows pan"), "minimap toggle has a keyboard shortcut and accessible navigation guidance");
        map.Focus(); Key(window, Avalonia.Input.Key.M, RawInputModifiers.Control | RawInputModifiers.Shift);
        check(!Control<Border>("MinimapPanel").IsVisible && graph.IsFocused, "hiding a focused minimap with the shortcut returns focus to graph navigation");
        Key(window, Avalonia.Input.Key.M, RawInputModifiers.Control | RawInputModifiers.Shift);
        var requested = new Point(window.Project.ListOfCharacterStates[4].VisualX + 135, window.Project.ListOfCharacterStates[4].VisualY + 100);
        var mapPoint = map.TranslatePoint(map.WorldToMap(requested), window)!.Value;
        window.MouseDown(mapPoint, MouseButton.Left); window.MouseUp(mapPoint, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        check(((Vector)(graph.ViewportWorld.Center - requested)).Length < .001, "clicking the minimap centers the viewport at the requested world position");
        var centre = graph.ViewportWorld.Center; var zoom = graph.Zoom;
        map.Focus(); Key(window, Avalonia.Input.Key.Right); check(Math.Abs(graph.ViewportWorld.Center.X - centre.X - 60 / zoom) < .001, "minimap arrow keys pan the viewport without moving scenes");
        Key(window, Avalonia.Input.Key.Down, RawInputModifiers.Shift); check(Math.Abs(graph.ViewportWorld.Center.Y - centre.Y - 160 / zoom) < .001, "minimap Shift-arrow pans farther in viewport units");
        Key(window, Avalonia.Input.Key.Escape); check(graph.IsFocused, "minimap Escape returns keyboard focus to the graph");
        centre = graph.ViewportWorld.Center; Key(window, Avalonia.Input.Key.Left, RawInputModifiers.Alt);
        check(Math.Abs(graph.ViewportWorld.Center.X - centre.X + 60 / zoom) < .001, "graph Alt-arrow provides viewport navigation while the minimap is hidden or unfocused");
        map.Focus(); Key(window, Avalonia.Input.Key.Enter); var fitViewport = graph.ViewportWorld;
        var fromWorld = fitViewport.Center; var endWorld = fromWorld + new Vector(50, 30);
        var fromMap = map.TranslatePoint(map.WorldToMap(fromWorld), window)!.Value; var endMap = map.TranslatePoint(map.WorldToMap(endWorld), window)!.Value;
        window.MouseDown(fromMap, MouseButton.Left); window.MouseMove(endMap); window.MouseUp(endMap, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        check(((Vector)(graph.ViewportWorld.Center - endWorld)).Length < .001, "minimap drag keeps a stable world transform for predictable navigation");
        check(ProjectStore.Serialize(window.Project) == before && !window.HasUnsavedChanges && graph.SelectedScenes.Select(s => s.State).SequenceEqual(selectedIds) && graph.Selected!.State == activeId, "all minimap gestures preserve scene positions, active scene, selection, dirty state and history");
        Press(Control<Button>("ScriptButton")); check(!Control<Border>("MinimapPanel").IsVisible, "script view hides the minimap");
        Press(Control<Button>("GraphButton")); check(Control<Border>("MinimapPanel").IsVisible, "returning to graph restores the minimap visibility preference");
        foreach (var theme in new[] { "Dark", "Light", "High contrast" })
            foreach (var scale in new[] { 1d, 1.15, 1.3 })
            {
                window.ChangeAppearance(theme, scale); window.Width = 1540 * scale; window.Height = 940 * scale; Dispatcher.UIThread.RunJobs(); graph.Fit();
                var render = "groups-minimap-" + theme.Replace(' ', '-') + "-" + (int)Math.Round(scale * 100);
                capture(window, render);
                window.Width = 1000 * scale; window.Height = 680 * scale; Dispatcher.UIThread.RunJobs(); graph.Fit(); capture(window, render + "-compact");
                var panel = Control<Border>("MinimapPanel"); var hint = Control<Border>("GraphHint");
                check(panel.Bounds.Right <= graph.Bounds.Width && panel.Bounds.Bottom <= graph.Bounds.Height && hint.Bounds.Right < panel.Bounds.Left && Control<ListBox>("SceneList").Bounds.Height > 30, "compact minimap, selection hint and filtered outline fit at " + theme + " " + scale);
                check(graph.SelectedScenes.Count == 2 && graph.Selected!.State == activeId && Control<StackPanel>("EditorPanel").Bounds.Width > 260, "groups retain the active editor and scene set at " + theme + " " + scale);
                dialog = Groups(); capture(dialog, render + "-dialog");
                dialog.Width = 560 * scale; dialog.Height = 360 * scale; Dispatcher.UIThread.RunJobs(); capture(dialog, render + "-dialog-compact");
                Named<TextBox>(dialog, "SceneGroupName").Focus(); for (var tab = 0; tab < 6; tab++) Key(dialog, Avalonia.Input.Key.Tab);
                var filterAction = Named<Button>(dialog, "FilterSceneGroup"); var actionBottom = filterAction.TranslatePoint(new Point(0, filterAction.Bounds.Height), dialog)!.Value.Y;
                check(dialog.MinWidth == 560 * scale && dialog.MinHeight == 360 * scale && Named<ListBox>(dialog, "SceneGroupList").Bounds.Height > 40 && Named<TextBox>(dialog, "SceneGroupName").Bounds.Width > 100 && filterAction.IsFocused && actionBottom < dialog.Height - 60 * scale, "compact group manager keeps its list usable and Tab scrolls later actions into view at " + theme + " " + scale); Close(dialog);
            }
        window.ChangeAppearance("Dark"); window.Width = 1540; window.Height = 940; Dispatcher.UIThread.RunJobs();
        Press(Control<Button>("HideMinimap")); check(!Control<Border>("MinimapPanel").IsVisible && graph.IsFocused && ProjectStore.Serialize(window.Project) == before, "minimap close restores graph focus without a document edit");
        graph.Focus(); Key(window, Avalonia.Input.Key.M, RawInputModifiers.Meta | RawInputModifiers.Shift);
        var preferences = new WorkspaceStore(Path.Combine(folder, "settings")).Load(); check(preferences.ShowMinimap, "minimap visibility persists in workspace preferences and supports Cmd shortcut");
        Pump(window.OpenProjectAsync(path)); check(window.Project.SceneGroups.Count == 2 && Control<ComboBox>("GroupFilter").SelectedItem!.ToString() == "Any group" && graph.SelectedScenes.Count == 1, "reopening groups clears transient filters and previous selection consistently");
        var emptyPath = Path.Combine(folder, "empty.dialogue.json"); ProjectStore.AtomicWrite(emptyPath, ProjectStore.Serialize(new DialogueProject())); Pump(window.OpenProjectAsync(emptyPath));
        capture(window, "groups-minimap-empty"); map.Focus(); Key(window, Avalonia.Input.Key.Home); Key(window, Avalonia.Input.Key.Up);
        check(graph.Project.ListOfCharacterStates.Count == 0 && double.IsFinite(graph.ViewportWorld.Center.X) && double.IsFinite(graph.ViewportWorld.Center.Y) && !window.HasUnsavedChanges, "empty-story minimap renders and navigates without invalid geometry or document changes");
        window.Close();
        var timings = new List<string>();
        foreach (var count in new[] { 1000, 10000 })
        {
            var large = new DialogueProject { ListOfCharacterStates = Enumerable.Range(0, count).Select(i => Templates.Scene("map_" + i, "Scene " + i, "Character", "", i % 100 * 370, i / 100 * 340)).ToList() };
            for (var i = 0; i < count - 1; i++) large.ListOfCharacterStates[i].DefaultNextId = large.ListOfCharacterStates[i + 1].State;
            var largeGraph = new GraphCanvas { Project = large }; largeGraph.Measure(new Size(800, 600)); largeGraph.Arrange(new Rect(0, 0, 800, 600));
            largeGraph.SetSelection([large.ListOfCharacterStates[0], large.ListOfCharacterStates[^1]], large.ListOfCharacterStates[^1]);
            var largeMap = new GraphMinimap { Graph = largeGraph }; var overview = new Window { Width = 260, Height = 170, Content = largeMap };
            var watch = System.Diagnostics.Stopwatch.StartNew(); overview.Show(); capture(overview, "minimap-" + count + "-scenes"); watch.Stop();
            timings.Add($"{count} scenes: {watch.Elapsed.TotalMilliseconds:F1} ms to show and capture a standalone minimap (includes headless scheduling and PNG encoding).");
            check(double.IsFinite(largeMap.WorldToMap(new Point(largeGraph.StoryBounds.Right, largeGraph.StoryBounds.Bottom)).X) && largeGraph.SelectedScenes.Count == 2, "minimap renders indexed connections and selected endpoints for " + count + " scenes");
            overview.Close();
        }
        File.WriteAllLines(Path.Combine(output, "minimap-render-probes.txt"), timings);
    }
    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static T Pump<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("Group UI operation did not complete.");
        var result = task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); return result;
    }
}
