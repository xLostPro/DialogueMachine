using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine;
using DialogueMachine.Core;
using DialogueMachine.UI;

internal static class EditorPerformanceRegressions
{
    // Deterministic large story shared by the scale regressions below.
    public static DialogueProject Fixture(int count)
    {
        var project = new DialogueProject { ProjectName = $"Performance {count}", CharacterId = "performance", EntryStateId = "scene_00000" };
        project.InitialVariables["aff:trust"] = 1;
        var extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"FutureData\":{\"version\":2,\"label\":\"preserved\"}}")!;
        project.Extra = extra;
        for (var i = 0; i < count; i++)
        {
            string Id(int index) => $"scene_{index:D5}";
            var scene = Templates.Scene(Id(i), $"Chapter {i / 50:D3} · Scene {i:D5}", i % 2 == 0 ? "Ada" : "Mara",
                string.Join('\n', Enumerable.Repeat("[b]The harbour[/b] waits for {player_name}. [if aff:trust >= 1]Welcome back.[else]Who are you?[/if] A perf-marker on the old map leads across the water.", i % 20 == 0 ? 12 : 1)),
                i % 20 * 360 - 360, i / 20 * 520 - 520);
            scene.Metadata["notes"] = "Follow the perf-marker; preserve the cycle and the ending.";
            scene.Metadata["tags"] = i % 5 == 0 ? "checkpoint, harbour" : "harbour";
            scene.Extra = i % 10 == 0 ? extra : null;
            scene.DefaultNextId = i == count - 1 ? "[EXIT]" : Id(i + 1);
            var choices = new[] { 0, 1, 2, 3, 5 }[i % 5];
            for (var c = 0; c < choices; c++)
            {
                var bytes = new byte[16]; BitConverter.GetBytes(i).CopyTo(bytes, 0); BitConverter.GetBytes(c).CopyTo(bytes, 4);
                scene.Choices.Add(new Choice { InternalId = new Guid(bytes), ButtonText = $"Route {c}: follow the perf-marker at scene {i:D5}",
                    NextStateId = c == 0 ? null : c == 1 ? Id(Math.Max(0, i - 19)) : Id((i + c * 7) % count), Visibility = (VisibilityMode)(c % 3),
                    Requirements = c % 2 == 0 ? [] : [new() { TargetId = "trust", Value = "1" }], Extra = c == 0 ? extra : null });
            }
            project.ListOfCharacterStates.Add(scene);
        }
        for (var start = 0; start < count; start += 50)
            project.SceneGroups.Add(new SceneGroup { Id = $"group_{start:D5}", Name = $"Chapter {start / 50:D3}",
                Scenes = project.ListOfCharacterStates.Skip(start).Take(60).Select(s => new SceneReference { StateId = s.State }).ToList(), Extra = extra });
        project.SceneTemplates.Add(new() { Name = "Opening", Scene = ProjectStore.Clone(new DialogueProject { ListOfCharacterStates = [project.ListOfCharacterStates[0]] }).ListOfCharacterStates[0] });
        project.ChoiceTemplates.Add(new() { Name = "Continue", Choice = new() { InternalId = Guid.Empty, ButtonText = "Continue with {player_name}", Visibility = VisibilityMode.Always } });
        return project;
    }

    public static void Run(Action<bool, string> check, string output, Action<Window, string> capture)
    {
        var fixture = Fixture(100);
        check(ProjectStore.Serialize(fixture) == ProjectStore.Serialize(Fixture(100)), "performance fixtures have deterministic IDs, content, groups and JSON");
        check(fixture.ListOfCharacterStates.Select(s => s.Choices.Count).Distinct().Count() == 5 && fixture.ListOfCharacterStates[0].Text.Split('\n').Length == 12 && fixture.SceneGroups.Count == 2,
            "performance fixtures include varied choice counts, long dialogue and overlapping groups");
        var historic = new ProjectHistory(); historic.Reset(fixture); var snapshots = new List<string> { historic.CurrentSnapshot };
        for (var i = 0; i < 105; i++) { fixture.ListOfCharacterStates[0].Text += $" 😀 {i}"; historic.Commit(fixture); snapshots.Add(ProjectStore.Serialize(fixture)); }
        for (var i = 104; i >= 5; i--) { historic.Undo(); if (historic.CurrentSnapshot != snapshots[i]) throw new Exception("Compressed Undo lost project JSON"); }
        check(!historic.CanUndo && historic.CurrentSnapshot == snapshots[5], "compressed complete snapshots retain the exact 100-entry Undo boundary");
        for (var i = 6; i <= 105; i++) { var restored = historic.Redo()!; if (ProjectStore.Serialize(restored) != snapshots[i]) throw new Exception("Compressed Redo lost project JSON"); }
        check(!historic.CanRedo && historic.CurrentSnapshot == snapshots[^1], "compressed history preserves Unicode, unknown fields, templates, groups and all Redo entries");
        historic.Undo(); fixture = ProjectStore.Parse(historic.CurrentSnapshot); check(!historic.Commit(fixture) && historic.CanRedo, "no-op commits retain Redo with compressed snapshots");
        fixture.ProjectName = "A new branch"; historic.Commit(fixture); check(!historic.CanRedo, "new edits discard only the previous Redo branch");
        historic.Reset(fixture); check(!historic.CanUndo && !historic.CanRedo, "history reset releases both compressed stacks");

        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 400));
        var crossing = new DialogueProject { ListOfCharacterStates = [Templates.Scene("left", "Left", "Ada", "Left", -1100, 50), Templates.Scene("right", "Right", "Mara", "Right", 1100, 50)] };
        crossing.ListOfCharacterStates[0].DefaultNextId = "right";
        var canvas = new GraphCanvas { Project = crossing }; canvas.Measure(new Size(800, 400)); canvas.Arrange(new Rect(0, 0, 800, 400)); canvas.SetZoom(1); canvas.CentreWorld(new Point(400, 200));
        bitmap.Render(canvas);
        check(canvas.RenderedNodes == 0 && canvas.RenderedLinks == 1, "offscreen graph nodes are culled while a link crossing the viewport remains drawn");
        foreach (var (a, b) in new[] { (new Point(-1000, 40), new Point(1000, 150)), (new Point(100, 250), new Point(-20, 80)), (new Point(270, 135), new Point(0, 35)) })
        {
            var hull = GraphCanvas.ConnectionHull(a, b); var curve = GraphCanvas.ConnectionCurve(a, b).Bounds;
            check(hull.Contains(curve.TopLeft) && hull.Contains(curve.BottomRight), "conservative connection bounds retain forward, backward and self-link curves");
        }
        canvas.Project = new DialogueProject { ListOfCharacterStates = [Templates.Scene("port", "Visible port", "Ada", "Text", 405.5, 50)] };
        canvas.SetZoom(2); canvas.CentreWorld(new Point(200, 100)); bitmap.Render(canvas);
        check(canvas.RenderedNodes == 1, "maximum-zoom culling retains an offscreen node whose port stroke intersects the viewport");
        var exportCanvas = new GraphCanvas { Project = crossing, ExportPlan = new(new Rect(-1200, 0, 2600, 400), new PixelSize(800, 400), .3, true) };
        exportCanvas.Measure(new Size(800, 400)); exportCanvas.Arrange(new Rect(0, 0, 800, 400)); bitmap.Render(exportCanvas);
        check(exportCanvas.RenderedNodes == 2 && exportCanvas.RenderedLinks == 1, "full graph exports bypass viewport culling and retain every scene and connection");
        exportCanvas.ClearTextLayouts();
        var stress = new GraphCanvas { Project = Fixture(1000), ExportPlan = new(new Rect(0, 0, 800, 400), new PixelSize(800, 400), .02, true) };
        stress.Measure(new Size(800, 400)); stress.Arrange(new Rect(0, 0, 800, 400)); bitmap.Render(stress);
        check(stress.RenderedNodes == 1000 && stress.CachedTextLayouts <= GraphTextCache.Capacity && stress.CachedTextCharacters <= GraphTextCache.CharacterBudget, "graph text caches stay bounded while drawing a complete 1,000-scene overview");
        var salt = new GraphCanvas { Project = ProjectStore.Load("Examples/salt-ledger.dialogue.json"), Width = 1400, Height = 860 }; salt.Measure(new Size(1400, 860)); salt.Arrange(new Rect(0, 0, 1400, 860)); salt.Fit();
        using var overview = new RenderTargetBitmap(new PixelSize(1400, 860)); overview.Render(salt); overview.Save(Path.Combine(output, "graph-zoomed-out.png"));
        check(salt.Zoom < .55 && salt.RenderedNodes == salt.Project.ListOfCharacterStates.Count, "a fitted large story renders every scene in zoomed-out overview mode");
        canvas.Project = stress.Project; stress.ClearTextLayouts();
        canvas.SetZoom(1); canvas.Centre(canvas.Project.ListOfCharacterStates[0]);
        byte[] Pixels() { bitmap.Render(canvas); using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); return SHA256.HashData(stream.ToArray()); }
        var before = Pixels(); canvas.Project.ListOfCharacterStates[0].Text = "Changed after drawing";
        check(!Pixels().SequenceEqual(before), "content keys prevent stale graph text after an in-place scene edit");
        var oldProject = canvas.Project; canvas.Project = ProjectStore.Clone(oldProject);
        check(canvas.CachedTextLayouts == 0, "history and reload document replacement release old graph text layouts");
        bitmap.Render(canvas); var dark = Pixels(); ((App)Application.Current!).SetPalette("Light");
        check(!Pixels().SequenceEqual(dark), "palette changes invalidate cached text and update graph colours");
        ((App)Application.Current!).SetPalette("Dark"); canvas.ClearTextLayouts();

        var folder = Path.Combine(output, "performance-regressions"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "story.dialogue.json"); ProjectStore.AtomicWrite(path, ProjectStore.Serialize(Fixture(1000)));
        var window = new MainWindow(null, true); window.Show(); window.OpenProjectAsync(path, false).GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        T C<T>(string name) where T : Control => window.FindControl<T>(name)!;
        void Press(string name) { Dispatcher.UIThread.RunJobs(DispatcherPriority.Normal); C<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        var list = C<ListBox>("SceneList"); var graph = C<GraphCanvas>("Graph");
        check(list.Items.Count == 1000 && list.GetVisualDescendants().OfType<ListBoxItem>().Count() < 100, "outline retains every scene while materializing only visible rows");
        check(C<ListBox>("ScriptList").ItemsSource is null, "hidden script content does not retain a whole-story control tree");
        list.SelectedItem = list.Items.Cast<NavigationScene>().Single(s => s.Index == 950); Dispatcher.UIThread.RunJobs();
        check(graph.Selected == window.Project.ListOfCharacterStates[950], "virtualized outline can select an unrealized scene and open its editor");
        list.ScrollIntoView(list.SelectedItem); Dispatcher.UIThread.RunJobs();
        check(list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Scene 00950") == true), "outline recycling displays the correct far-away scene label");
        TextBox Dialogue() => C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Dialogue text");
        var original = Dialogue().Text!; Dialogue().Text = "one  two\nthree\t four"; Dispatcher.UIThread.RunJobs(DispatcherPriority.Normal);
        var expectedWords = window.Project.ListOfCharacterStates.Sum(s => s.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        check(window.HasUnsavedChanges && C<TextBlock>("StatsText").Text!.EndsWith($"{expectedWords} words"), "typing updates exact dirty queries and whitespace word counts without a whole-document split");
        Dialogue().Text = original; Dispatcher.UIThread.RunJobs(DispatcherPriority.Normal); check(!window.HasUnsavedChanges, "reverting pending text restores exact clean state before commit"); Press("ContentTab");
        check(!C<Button>("UndoButton").IsEnabled && !C<TextBlock>("ProjectTitle").Text!.EndsWith(" •"), "a reverted typing batch creates no phantom Undo or committed dirty marker");
        C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBox>().First().Text = "Renamed visible title"; Press("ContentTab");
        check(list.Items.Cast<NavigationScene>().Single(s => s.Index == 950).Title == "Renamed visible title", "outline snapshots refresh edited labels after commit");
        Press("UndoButton"); check(list.Items.Cast<NavigationScene>().Single(s => s.Index == 950).Title != "Renamed visible title" && !window.HasUnsavedChanges, "Undo refreshes virtualized labels, statistics and saved equality");
        Press("RedoButton"); Press("ScriptButton");
        check(C<ListBox>("ScriptList").Items.Count == 1000 && C<ListBox>("ScriptList").GetVisualDescendants().OfType<ListBoxItem>().Count() < 100, "script view lazily includes every scene after history changes while virtualizing item containers");
        Press("GraphButton"); Dialogue().Text = "New script text"; Press("ContentTab"); Press("ScriptButton");
        var scriptList = C<ListBox>("ScriptList");
        scriptList.ScrollIntoView(scriptList.Items.Cast<MainWindow.ScriptRow>().Single(r => r.Index == 950)); Dispatcher.UIThread.RunJobs();
        check(scriptList.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "New script text"), "opening script after editing refreshes deferred content");
        Press("GraphButton"); window.SaveProjectAsync().GetAwaiter().GetResult();
        check(!window.HasUnsavedChanges && C<Button>("UndoButton").IsEnabled && ProjectFiles.Read(path).Parse().ListOfCharacterStates[950].Text == "New script text", "cached committed snapshots save current edits without discarding history");
        void PickerStep(ComboBox picker, string value, string label)
        {
            var old = ProjectStore.Serialize(window.Project); picker.SelectedItem = value; Dispatcher.UIThread.RunJobs(); var next = ProjectStore.Serialize(window.Project);
            Press("UndoButton"); check(old != next && ProjectStore.Serialize(window.Project) == old, label + " marks a pending edit and records one complete Undo step");
            Press("RedoButton"); check(ProjectStore.Serialize(window.Project) == next, label + " preserves its Redo step"); Press("UndoButton");
        }
        Press("LogicTab"); PickerStep(C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Scene type"), "Shop", "scene type picker");
        list.SelectedItems!.Clear(); list.SelectedItem = list.Items.Cast<NavigationScene>().Single(s => s.Index == 953); Dispatcher.UIThread.RunJobs();
        PickerStep(C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<ComboBox>().Single(c => c.Items.Count == 2 && c.Items.Contains("Affinity")), "Inventory", "condition type picker");
        C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "＋ Add action")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        PickerStep(C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Action type"), "Inventory", "action type picker");
        C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Action type").SelectedItem = "Set flag"; Dispatcher.UIThread.RunJobs();
        check(window.Project.ListOfCharacterStates[953].DefaultAfterActions.Concat(window.Project.ListOfCharacterStates[953].Choices.SelectMany(c => c.AfterChoiceActions)).Any(a => a.Type == "Set") && C<StackPanel>("EditorPanel").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Set flag to"), "choosing Set flag stores a Set action and shows the on/off picker instead of a raw integer"); Press("UndoButton");
        Press("UndoButton"); list.SelectedItems!.Clear(); list.SelectedItem = list.Items.Cast<NavigationScene>().Single(s => s.Index == 950); Dispatcher.UIThread.RunJobs(); Press("ContentTab");
        foreach (var theme in new[] { "Dark", "Light", "High contrast" })
            foreach (var scale in new[] { 1d, 1.15, 1.3 })
            {
                window.ChangeAppearance(theme, scale); window.Width = 1540 * scale; window.Height = 940 * scale; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); graph.SetZoom(.85); graph.Centre(window.Project.ListOfCharacterStates[950]);
                var name = "performance-" + theme.ToLowerInvariant().Replace(' ', '-') + "-" + (int)Math.Round(scale * 100); capture(window, name);
                check(graph.CachedTextLayouts > 0 && graph.CachedTextLayouts <= GraphTextCache.Capacity, "graph text cache repopulates correctly at " + theme + " " + scale);
                window.Width = 1000 * scale; window.Height = 680 * scale; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); graph.Centre(window.Project.ListOfCharacterStates[950]); capture(window, name + "-compact");
                check(list.Bounds.Height > 30 && graph.Bounds.Width > 250, "virtualized outline and culled graph remain usable in compact " + theme + " " + scale);
            }
        var smallerPath = Path.Combine(folder, "smaller.dialogue.json"); ProjectStore.AtomicWrite(smallerPath, ProjectStore.Serialize(Templates.Harbour()));
        window.OpenProjectAsync(smallerPath, false).GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        check(list.Items.Count == 5 && graph.Project.ListOfCharacterStates.Count == 5 && !list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Renamed visible title"), "loading a smaller story discards recycled outline labels and graph text from the previous document");
        window.Close();
    }
}
