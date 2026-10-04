using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogueMachine.Core;
using DialogueMachine.UI;
using System.Globalization;
using System.Text.Json;

internal static class ExportChecks
{
    private static DialogueProject Story()
    {
        var a = Templates.Scene("first", "Tall first scene", "Mara", "[if aff:trust >= 1]Friend[else][if inv:coin > 0]Buyer[else]Stranger[/if][/if]\nSecond page", -640, -500);
        var b = Templates.Scene("entry", "Opening", "Guide", "Hello {player_name}\nNext page", 100, -450);
        var c = Templates.Scene("cycle", "Cycle", "Mara", "Around again", -640, 260);
        var d = Templates.Scene("remote", "Unreachable scene", "Guide", "Far outside the viewport", 550, 550);
        var e = Templates.Scene("unset", "Unfinished", "Guide", "\n", 100, 220);
        a.Choices = Enumerable.Range(0, 12).Select(i => new Choice { ButtonText = "Choice " + (i + 1), NextStateId = i == 0 ? "first" : i == 1 ? "[EXIT]" : "entry", Visibility = (VisibilityMode)(i % 3) }).ToList();
        a.DefaultNextId = "cycle"; b.DefaultNextId = "[EXIT]"; c.DefaultNextId = "first"; d.DefaultNextId = "first"; e.DefaultNextId = null;
        b.Choices = [new() { ButtonText = "Inherited", NextStateId = " \t" }, new() { ButtonText = "Missing", NextStateId = "missing" }];
        e.Choices = [new() { ButtonText = "Unset inherited", NextStateId = null }];
        a.DefaultAfterActions = [new() { TargetId = "trust", Amount = -2 }, new() { Type = "Script", TargetId = "hook", Parameter = "<tag> **literal**" }];
        a.Choices[0].Requirements = [new() { TargetId = "trust", Operator = ">=", Value = "1" }, new() { Type = "Inventory", TargetId = "coin", Operator = "!=", Value = "0" }];
        a.Choices[0].AfterChoiceActions = [new() { Type = "Event", TargetId = "hello", Parameter = "one\ntwo" }, new() { Type = "Inventory", TargetId = "coin", Amount = 1 }];
        a.Metadata["notes"] = "Private **notes**\n# Not a heading <script>&";
        d.SayItAll = false;
        var project = new DialogueProject { ProjectName = "Export fixtures", EntryStateId = "entry", ListOfCharacterStates = [a, b, c, d, e] };
        SceneGroups.Assign(project, SceneGroups.Create(project, "Act I"), [a, c]);
        var extra = JsonSerializer.SerializeToElement(new { retained = 42 });
        project.Extra = new() { ["FutureRoot"] = extra }; a.Extra = new() { ["FutureScene"] = extra };
        a.Choices[0].Extra = new() { ["FutureChoice"] = extra }; a.Choices[0].Requirements[0].Extra = new() { ["FutureRequirement"] = extra };
        a.DefaultAfterActions[0].Extra = new() { ["FutureAction"] = extra }; project.SceneGroups[0].Extra = new() { ["FutureGroup"] = extra };
        return project;
    }

    public static void Core(Action<bool, string> check, Action<Action, string> throws, string output)
    {
        var story = Story(); var before = ProjectStore.Serialize(story);
        var text = ScriptExport.Markdown(story, new(IncludeNotes: true));
        File.WriteAllText(Path.Combine(output, "full-story-script.md"), text);
        check(text.IndexOf("## Opening [entry]", StringComparison.Ordinal) < text.IndexOf("## Tall first scene [first]", StringComparison.Ordinal)
            && text.IndexOf("## Cycle", StringComparison.Ordinal) < text.IndexOf("## Unreachable", StringComparison.Ordinal), "script ordering is entry first, then stable project order including unreachable scenes");
        check(text.Contains("**Page 1:**") && text.Contains("**Page 2:** Second page") && text.Contains("**Speaker:** Mara") && text.Contains("**Candidate 1:** Far outside"), "script shows speakers, separate ordered pages and random candidates");
        check(text.Contains(@"\[if aff:trust &gt;\= 1\]Friend\[else\]\[if inv:coin &gt; 0\]Buyer\[else\]Stranger\[/if\]\[/if\]") && text.Contains(@"\{player\_name\}"), "script preserves nested inline conditions, both branches and literal substitutions without evaluation");
        check(text.Contains("1. Choice 1 → Tall first scene [first]") && text.Contains("12. Choice 12 → Opening [entry]"), "script retains ordered choices, IDs, destinations, cycles and self-links");
        check(text.Contains("Inherited continue → End conversation") && text.Contains("Missing target [missing]") && text.Contains("Inherited continue → Unset destination") && text.Contains("**Continue →** Unset destination"), "script distinguishes whitespace/null fallback, endings, unset and missing destinations");
        check(text.Contains("Hide (0)") && text.Contains("Disable (1)") && text.Contains("Always (2)") && text.Contains(@"Conditions (all must pass): Affinity trust &gt;\= 1; Inventory coin \!\= 0"), "script shows numeric website visibility order and AND requirements");
        check(text.Contains("Response actions (in order)") && text.Contains("Scene exit actions (after response actions, in order)") && text.Contains(@"add \-2") && text.Contains(@"add \+1") && text.IndexOf("Event hello", StringComparison.Ordinal) < text.IndexOf("Inventory coin", text.IndexOf("Event hello", StringComparison.Ordinal), StringComparison.Ordinal), "script documents response-before-scene action order and preserves amounts, hooks and parameters");
        check(text.Contains(@"Private \*\*notes\*\*") && text.Contains(@"\# Not a heading &lt;script&gt;&amp;"), "script safely escapes notes, HTML, Markdown headings and emphasis");
        for (var bits = 0; bits < 16; bits++)
        {
            var options = new ScriptExportOptions((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0);
            var result = ScriptExport.Markdown(story, options);
            check(result.Contains("Visibility:") == options.IncludeConditionsAndVisibility && result.Contains("Conditions (all") == options.IncludeConditionsAndVisibility
                && result.Contains("Response actions (in order)") == options.IncludeResponseActions && result.Contains("Scene exit actions") == options.IncludeSceneActions
                && result.Contains("Private") == options.IncludeNotes && result.Contains("Friend") && result.Contains("Buyer") && result.Contains("Stranger"), "script option combination " + bits + " controls extras while retaining all inline branches");
        }
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); check(ScriptExport.Markdown(story, new(IncludeNotes: true)) == text, "script output is deterministic across cultures and repeated exports"); }
        finally { CultureInfo.CurrentCulture = culture; }
        var hostile = Templates.Blank(); hostile.ProjectName = "# * _ [x](https://example.com) `code` <img>& \\";
        hostile.ListOfCharacterStates[0].Text = "1. list\n> quote\n```\n![img](file)\n---";
        var escaped = ScriptExport.Markdown(hostile);
        check(escaped.Contains(@"\# \* \_ \[x\]\(https://example\.com\) \`code\` &lt;img&gt;&amp; \\") && escaped.Contains(@"1\. list") && escaped.Contains("&gt; quote") && escaped.Contains(@"\`\`\`") && escaped.Contains(@"\!\[img\]"), "script escapes punctuation, links, images, code fences, lists, quotes and author backslashes");
        check(!ScriptExport.Markdown(story).Contains("Private"), "writer's notes are excluded by default");
        check(text.Contains("10. Choice 10") && text.Contains("\n    Visibility: Hide (0)") && text.Contains("\n    **Response actions"), "double-digit choices indent their metadata inside the ordered Markdown item");
        var multiline = Templates.Blank(); multiline.ProjectName = "Story\n# Injected"; multiline.ListOfCharacterStates[0].Metadata["title"] = "Title\n## Injected";
        multiline.ListOfCharacterStates[0].Choices = [new() { ButtonText = "[if aff:trust > 0]Yes[else]No[/if]\nSecond line", NextStateId = "[EXIT]" }];
        var multiScript = ScriptExport.Markdown(multiline, new(false, false, false, false));
        check(multiScript.StartsWith("# Story \\# Injected\n\n") && multiScript.Contains("## Title \\#\\# Injected [") && multiScript.Contains(@"\[if aff:trust &gt; 0\]Yes\[else\]No\[/if\]") && multiScript.Contains("\n   Second line"), "multiline titles cannot inject headings and multiline conditional responses retain both branches within their choice");
        var duplicate = Templates.Scene("first", "Duplicate repairable scene", "Guide", "Duplicate text", 0, 0); story.ListOfCharacterStates.Add(duplicate);
        check(ScriptExport.Markdown(story).Contains("## Duplicate repairable scene [first]"), "script includes repairable duplicate-ID scenes without traversal or deduplication"); story.ListOfCharacterStates.Remove(duplicate);
        var plan = GraphImageExport.Plan(story);
        check(plan.WorldBounds.X < -640 && plan.WorldBounds.Y < -500 && plan.WorldBounds.Bottom >= 550 + GraphGeometry.NodeHeight(story.ListOfCharacterStates[3]) + GraphImageExport.Padding, "graph bounds include negative positions and all distant scenes");
        check(plan.WorldBounds.Right > 550 + GraphGeometry.NodeWidth + GraphImageExport.Padding, "graph bounds include backward-cycle Bezier excursions beyond the rightmost scene");
        check(plan.WorldBounds.Bottom > story.ListOfCharacterStates[0].VisualY + GraphGeometry.NodeHeight(story.ListOfCharacterStates[0]) && GraphGeometry.NodeHeight(story.ListOfCharacterStates[0]) == 566, "full graph uses actual tall scene dimensions");
        check(GraphImageExport.Plan(story, 2).Pixels.Width == plan.Pixels.Width * 2 || Math.Abs(GraphImageExport.Plan(story, 2).Pixels.Width - plan.Pixels.Width * 2) == 1, "detailed graph export doubles world resolution independently of viewport");
        var huge = Templates.Blank(); huge.ListOfCharacterStates[0].VisualX = 0; huge.ListOfCharacterStates.Add(Templates.Scene("far", "Far", "Guide", "", 20000, 0));
        throws(() => GraphImageExport.Plan(huge), "oversized graph requires explicit fitting instead of unbounded bitmap allocation");
        var fit = GraphImageExport.Plan(huge, 2, true);
        check(fit.Reduced && fit.Scale >= GraphImageExport.MinScale && fit.Pixels.Width <= GraphImageExport.MaxDimension && (long)fit.Pixels.Width * fit.Pixels.Height <= GraphImageExport.MaxPixels, "fit reduces resolution while honoring side, pixel and readability limits");
        huge.ListOfCharacterStates[1].VisualY = 12000;
        fit = GraphImageExport.Plan(huge, 2, true);
        check((long)fit.Pixels.Width * fit.Pixels.Height <= GraphImageExport.MaxPixels && fit.Pixels.Height <= GraphImageExport.MaxDimension, "fitting also enforces area limits on wide and tall graphs");
        huge.ListOfCharacterStates[1].VisualX = 100000;
        throws(() => GraphImageExport.Plan(huge, 1, true), "unreadably large graphs are rejected even with fitting");
        throws(() => GraphImageExport.Plan(new()), "empty graph export reports that a scene is required");
        throws(() => GraphImageExport.Plan(story, double.NaN), "invalid graph export resolutions are rejected");
        var bad = Templates.Blank(); bad.ListOfCharacterStates[0].VisualX = double.NaN;
        throws(() => GraphImageExport.Plan(bad), "nonfinite scene geometry is rejected without editor mutation");
        var ending = Templates.Blank(); ending.ListOfCharacterStates.Add(Templates.Scene("[EXIT]", "Reserved scene", "Guide", "", 1000, 0));
        check(GraphImageExport.Plan(ending).WorldBounds.Right == 1000 + GraphGeometry.NodeWidth + GraphImageExport.Padding, "intentional endings do not draw connections to repairable reserved-ID scenes");
        check(ProjectStore.Serialize(story) == before, "both export services preserve complete project JSON, groups and unknown-field/history inputs");
    }

    public static void UI(Action<bool, string> check, string output, Action<Window, string> capture)
    {
        var folder = Path.Combine(output, "export-workflows"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "story.dialogue.json"); ProjectStore.AtomicWrite(path, ProjectStore.Serialize(Story()));
        var picker = new ExportPicker(); var window = new MainWindow(null, true, Path.Combine(folder, "settings"), picker); window.Show(); Pump(window.OpenProjectAsync(path));
        T Control<T>(string name) where T : Control => window.FindControl<T>(name)!;
        var graph = Control<GraphCanvas>("Graph");
        graph.SetSelection([window.Project.ListOfCharacterStates[0], window.Project.ListOfCharacterStates[2]], window.Project.ListOfCharacterStates[2]);
        graph.SetZoom(.6); graph.CentreWorld(new Point(9000, -7000));
        Control<TextBox>("SearchBox").Text = "Unreachable";
        Press(Control<Button>("ScriptButton"));
        var before = ProjectStore.Serialize(window.Project); var viewport = graph.ViewportWorld; var zoom = graph.Zoom; var selected = graph.Selected; var set = graph.SelectedScenes.ToArray();
        bool Unchanged() => ProjectStore.Serialize(window.Project) == before && graph.ViewportWorld == viewport && graph.Zoom == zoom && graph.Selected == selected
            && graph.SelectedScenes.SequenceEqual(set) && Control<ScrollViewer>("ScriptScroll").IsVisible && !graph.IsVisible && Control<TextBox>("SearchBox").Text == "Unreachable" && !window.HasUnsavedChanges && !Control<Button>("UndoButton").IsEnabled;
        Window Dialog() { Dispatcher.UIThread.RunJobs(); return window.OwnedWindows.Single(w => w.IsVisible); }
        var task = window.ExportGraphAsync(); Press(Named<Button>(Dialog(), "CancelExport")); check(!Pump(task) && Unchanged(), "graph options cancellation preserves script view, filters, selection, pan/zoom, positions and history");
        task = window.ExportGraphAsync(); Key(Dialog(), Avalonia.Input.Key.Escape); check(!Pump(task) && Unchanged(), "graph options Escape leaves the complete editor state unchanged");
        task = window.ExportGraphAsync(); Press(Named<Button>(Dialog(), "ConfirmExport")); check(!Pump(task) && Unchanged(), "graph file-picker cancellation leaves the complete editor state unchanged");
        var png = Path.Combine(output, "full-story-graph.png"); picker.Paths.Enqueue(png); task = window.ExportGraphAsync(); Press(Named<Button>(Dialog(), "ConfirmExport"));
        check(Pump(task) && Unchanged(), "successful full-story PNG export preserves script view, hidden selection, viewport, groups and history");
        var expected = GraphImageExport.Plan(window.Project);
        using (var bitmap = new Bitmap(png)) check(bitmap.PixelSize == expected.Pixels, "actual PNG dimensions match full-story bounds rather than hidden viewport dimensions");
        var reference = File.ReadAllBytes(png); Press(Control<Button>("GraphButton")); Control<TextBox>("SearchBox").Text = ""; graph.Fit(); graph.Focus();
        var alternate = Path.Combine(output, "full-story-graph-other-view.png"); GraphImageExport.Save(window.Project, alternate);
        check(File.ReadAllBytes(alternate).SequenceEqual(reference), "graph PNG bytes are independent of zoom, pan, focus, selection, outline filters and editor view");
        graph.SetZoom(zoom); graph.CentreWorld(viewport.Center); Control<TextBox>("SearchBox").Text = "Unreachable"; Press(Control<Button>("ScriptButton"));
        picker.Paths.Enqueue(folder); task = window.ExportGraphAsync(); Press(Named<Button>(Dialog(), "ConfirmExport")); Press(Dialog().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close")));
        check(!Pump(task) && Unchanged() && !Directory.GetFiles(folder, "*.tmp").Any(), "PNG write failure preserves editor state and cleans the temporary output");
        var md = Path.Combine(output, "script-with-notes.md"); picker.Paths.Enqueue(md); task = window.ExportScriptAsync(); Named<CheckBox>(Dialog(), "ExportNotes").IsChecked = true; Press(Named<Button>(Dialog(), "ConfirmExport"));
        check(Pump(task) && File.ReadAllText(md) == ScriptExport.Markdown(window.Project, new(IncludeNotes: true)) && Unchanged(), "script UI applies notes/options through Core and preserves editor state");
        task = window.ExportScriptAsync(); Named<CheckBox>(Dialog(), "ExportResponseActions").IsChecked = false; Key(Dialog(), Avalonia.Input.Key.Escape);
        check(!Pump(task) && Unchanged(), "script option drafts and Escape do not edit the project or history");
        task = window.ExportScriptAsync(); Press(Named<Button>(Dialog(), "ConfirmExport")); check(!Pump(task) && Unchanged(), "script file-picker cancellation leaves the editor unchanged");
        picker.Paths.Enqueue(folder); task = window.ExportScriptAsync(); Press(Named<Button>(Dialog(), "ConfirmExport")); Press(Dialog().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close")));
        check(!Pump(task) && Unchanged(), "script disk failure leaves the editor unchanged");
        foreach (var theme in new[] { "Dark", "Light", "High contrast" })
            foreach (var scale in new[] { 1d, 1.15, 1.3 })
            {
                window.ChangeAppearance(theme, scale); var name = "exports-" + theme.Replace(' ', '-') + "-" + (int)Math.Round(scale * 100);
                window.Width = 1540 * scale; window.Height = 940 * scale; Dispatcher.UIThread.RunJobs(); capture(window, name + "-workspace");
                window.Width = 1000 * scale; window.Height = 680 * scale; Dispatcher.UIThread.RunJobs(); capture(window, name + "-workspace-compact");
                GraphImageExport.Save(window.Project, Path.Combine(output, name + "-graph.png"));
                if (scale != 1) check(File.ReadAllBytes(Path.Combine(output, name + "-graph.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "exports-" + theme.Replace(' ', '-') + "-100-graph.png"))), "PNG resolution and rendering are independent of interface size at " + theme + " " + scale);
                foreach (var script in new[] { false, true })
                {
                    task = script ? window.ExportScriptAsync() : window.ExportGraphAsync(); var dialog = Dialog(); var dialogName = name + (script ? "-script-dialog" : "-image-dialog"); capture(dialog, dialogName);
                    dialog.Width = 440 * scale; dialog.Height = 340 * scale; Dispatcher.UIThread.RunJobs(); capture(dialog, dialogName + "-compact");
                    var button = Named<Button>(dialog, "ConfirmExport"); var cancel = Named<Button>(dialog, "CancelExport"); var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), dialog)!.Value;
                    check(button.IsEffectivelyVisible && bottom.Y < dialog.ClientSize.Height && bottom.X + button.Bounds.Width < dialog.ClientSize.Width && cancel.IsEffectivelyVisible
                        && dialog.GetVisualDescendants().OfType<ScrollViewer>().Single().Bounds.Height > 50, "compact " + (script ? "script" : "image") + " export keeps options scrollable and file/cancel actions visible at " + theme + " " + scale);
                    var inputs = script ? new Control[] { Named<CheckBox>(dialog, "ExportConditions"), Named<CheckBox>(dialog, "ExportResponseActions"), Named<CheckBox>(dialog, "ExportSceneActions"), Named<CheckBox>(dialog, "ExportNotes") }
                        : [Named<ComboBox>(dialog, "ExportResolution"), Named<CheckBox>(dialog, "ExportFitLimits")];
                    check(inputs.All(c => !string.IsNullOrEmpty(AutomationProperties.GetName(c))), "export options have accessible names at " + theme + " " + scale + " " + script);
                    inputs[^1].Focus(); Key(dialog, Avalonia.Input.Key.Tab);
                    check(cancel.IsFocused, "keyboard Tab reaches pinned export actions at " + theme + " " + scale + " " + script);
                    Key(dialog, Avalonia.Input.Key.Escape); Pump(task);
                }
            }
        window.ChangeAppearance("Dark");
        // Keep an existing redo stack intact without recording export as a document edit.
        Control<TextBox>("SearchBox").Text = ""; Press(Control<Button>("GraphButton")); Press(Control<Button>("LayoutButton")); Press(Control<Button>("UndoButton"));
        check(Control<Button>("RedoButton").IsEnabled, "export workflow fixture has redo history to preserve");
        task = window.ExportGraphAsync(); Press(Named<Button>(Dialog(), "CancelExport")); Pump(task);
        check(Control<Button>("RedoButton").IsEnabled && !window.HasUnsavedChanges, "canceling export preserves an existing redo stack");
        picker.Paths.Enqueue(png); task = window.ExportGraphAsync(); Press(Named<Button>(Dialog(), "ConfirmExport"));
        check(Pump(task) && Control<Button>("RedoButton").IsEnabled && !window.HasUnsavedChanges, "successful graph export preserves an existing redo stack");
        using (var locked = new FileStream(png, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var existing = File.ReadAllBytes(png); picker.Paths.Enqueue(png); task = window.ExportGraphAsync(); Press(Named<Button>(Dialog(), "ConfirmExport"));
            Press(Dialog().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close")));
            check(!Pump(task) && File.ReadAllBytes(png).SequenceEqual(existing) && Control<Button>("RedoButton").IsEnabled && !window.HasUnsavedChanges
                && !Directory.GetFiles(output, "*.tmp").Any(), "failed PNG replacement preserves the existing export, redo history and document and cleans temporary files");
        }
        window.Project.ListOfCharacterStates[0].VisualX = 100000;
        task = window.ExportGraphAsync(); var oversized = Dialog(); Named<CheckBox>(oversized, "ExportFitLimits").IsChecked = true;
        check(!Named<Button>(oversized, "ConfirmExport").IsEnabled && Named<TextBlock>(oversized, "ExportImageSummary").Text!.Contains("too large"), "unreadable oversized graph disables export with actionable guidance");
        capture(oversized, "export-image-too-large"); Key(oversized, Avalonia.Input.Key.Escape); Pump(task);
        window.Close();
    }

    private sealed class ExportPicker : IProjectFilePicker
    {
        public Queue<string?> Paths { get; } = new();
        public Task<string?> OpenAsync(Window owner) => Task.FromResult<string?>(null);
        public Task<string?> SaveAsync(Window owner, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> ExportAsync(Window owner, string title, string suggestedName, string extension) => Task.FromResult(Paths.Count > 0 ? Paths.Dequeue() : null);
    }
    private static T Named<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Key(Window window, Key key) { window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, ""); if (window.IsVisible) window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, ""); Dispatcher.UIThread.RunJobs(); }
    private static T Pump<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("Export operation did not complete.");
        var result = task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); return result;
    }
}
